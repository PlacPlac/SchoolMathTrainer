using System.Text.Json;
using SchoolMathTrainer.Api.Services;
using SharedCore.Models;
using SharedCore.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IOnlineDataService, ConfiguredApiDataService>();
builder.Services.AddSingleton<IClassDataRepository, FileClassDataRepository>();
builder.Services.AddSingleton<TeacherPasswordHasher>();
builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();
    var dataRoot = ResolveDataRoot(configuration["DataConnection:DataRoot"], environment.ContentRootPath);
    return new TeacherAccountStore(dataRoot, serviceProvider.GetRequiredService<TeacherPasswordHasher>());
});
builder.Services.AddSingleton(serviceProvider =>
{
    var teacherStore = serviceProvider.GetRequiredService<TeacherAccountStore>();
    return new StudentLoginLockoutStore(teacherStore.SecurityDirectory);
});
builder.Services.AddSingleton(serviceProvider =>
{
    var teacherStore = serviceProvider.GetRequiredService<TeacherAccountStore>();
    return new StudentSessionTokenService(teacherStore.SecurityDirectory);
});
builder.Services.AddSingleton<TeacherLoginLockoutStore>();
builder.Services.AddSingleton<TeacherTokenService>();
builder.Services.AddSingleton<TeacherAuthAuditLogger>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
});

var app = builder.Build();
var logger = app.Logger;

app.MapGet("/health", () =>
    Results.Ok(new HealthResponse("ok", "SchoolMathTrainer.Api", DateTime.UtcNow)));

IResult LoginTeacher(
    TeacherLoginRequest request,
    HttpContext httpContext,
    TeacherAccountStore teacherStore,
    TeacherLoginLockoutStore lockoutStore,
    TeacherTokenService tokenService,
    TeacherAuthAuditLogger audit)
{
    var username = request?.Username ?? string.Empty;
    var password = request?.Password ?? string.Empty;
    var remoteAddress = GetRemoteAddress(httpContext);

    try
    {
        if (lockoutStore.IsLocked(username, remoteAddress, out var retryAfter))
        {
            audit.Write("teacher_login_locked", username, remoteAddress, httpContext.Request.Path, StatusCodes.Status423Locked, "Teacher login is temporarily locked.");
            logger.LogWarning("Teacher login rejected because lockout is active from {RemoteAddress}.", remoteAddress);
            return CreateTeacherLoginLockedResult(httpContext, retryAfter);
        }

        var account = teacherStore.VerifyCredentials(username, password);
        if (account is null)
        {
            var failure = lockoutStore.RegisterFailure(username, remoteAddress);
            if (failure.LockoutStarted && failure.LockedUntilUtc.HasValue)
            {
                audit.Write("teacher_login_locked", username, remoteAddress, httpContext.Request.Path, StatusCodes.Status423Locked, "Teacher login was temporarily locked after repeated failed attempts.");
                logger.LogWarning("Teacher login lockout started from {RemoteAddress}.", remoteAddress);
                return CreateTeacherLoginLockedResult(httpContext, failure.LockedUntilUtc.Value - DateTime.UtcNow);
            }

            audit.Write("teacher_login_failed", username, remoteAddress, httpContext.Request.Path, StatusCodes.Status401Unauthorized, "Invalid teacher credentials.");
            logger.LogWarning("Teacher login failed from {RemoteAddress}.", remoteAddress);
            return Results.Unauthorized();
        }

        lockoutStore.RegisterSuccess(account.Username, remoteAddress);
        audit.Write("teacher_login_succeeded", account.Username, remoteAddress, httpContext.Request.Path, StatusCodes.Status200OK, "Teacher credentials accepted.");
        logger.LogInformation("Teacher login succeeded.");
        return Results.Ok(tokenService.IssueToken(account));
    }
    catch (ArgumentException ex)
    {
        var failure = lockoutStore.RegisterFailure(username, remoteAddress);
        if (failure.LockoutStarted && failure.LockedUntilUtc.HasValue)
        {
            audit.Write("teacher_login_locked", username, remoteAddress, httpContext.Request.Path, StatusCodes.Status423Locked, "Teacher login was temporarily locked after repeated failed attempts.");
            logger.LogWarning("Teacher login lockout started after invalid request from {RemoteAddress}.", remoteAddress);
            return CreateTeacherLoginLockedResult(httpContext, failure.LockedUntilUtc.Value - DateTime.UtcNow);
        }

        audit.Write("teacher_login_failed", username, remoteAddress, httpContext.Request.Path, StatusCodes.Status401Unauthorized, "Invalid teacher login request.");
        logger.LogWarning(ex, "Teacher login rejected because request was invalid.");
        return Results.Unauthorized();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Teacher login could not be completed.");
        return Results.Problem("Teacher login could not be completed.");
    }
}

app.MapPost("/api/teacher-auth/login", LoginTeacher);
app.MapPost("/api/teachers/login", LoginTeacher);

app.MapPost("/api/teachers/logout", (HttpContext httpContext, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    var remoteAddress = GetRemoteAddress(httpContext);
    if (!TryReadBearerToken(httpContext.Request, out var token))
    {
        audit.Write("teacher_unauthorized_access", string.Empty, remoteAddress, httpContext.Request.Path, StatusCodes.Status401Unauthorized, "Logout request did not include required authorization.");
        return Results.Unauthorized();
    }

    var validation = teacherTokens.ValidateToken(token);
    if (!validation.Success)
    {
        audit.Write("teacher_unauthorized_access", string.Empty, remoteAddress, httpContext.Request.Path, StatusCodes.Status401Unauthorized, validation.Message);
        return Results.Unauthorized();
    }

    teacherTokens.RevokeToken(token);
    audit.Write("teacher_logout", validation.Username, remoteAddress, httpContext.Request.Path, StatusCodes.Status200OK, "Teacher session revoked.");
    return Results.Ok(new ApiMessageResponse("Učitel byl odhlášen."));
});

app.MapGet("/api/admin/teachers", (HttpContext httpContext, TeacherAccountStore teacherStore, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeAdmin(httpContext, teacherTokens, audit, out var unauthorizedResult, out _))
    {
        return unauthorizedResult;
    }

    return Results.Ok(teacherStore.ListTeachers().Select(ToAdminTeacherListItem).ToList());
});

app.MapPost("/api/admin/teachers", (AdminCreateTeacherRequest request, HttpContext httpContext, TeacherAccountStore teacherStore, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeAdmin(httpContext, teacherTokens, audit, out var unauthorizedResult, out var admin))
    {
        return unauthorizedResult;
    }

    try
    {
        if (request is null || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Results.BadRequest(new ApiMessageResponse("Uživatelské jméno a zobrazované jméno jsou povinné."));
        }

        if (!TeacherUsernameRules.TryNormalize(request.Username, out _, out var usernameError))
        {
            return Results.BadRequest(new ApiMessageResponse(usernameError));
        }

        if (!TeacherPasswordRules.TryValidate(request.Password, out var passwordError))
        {
            return Results.BadRequest(new ApiMessageResponse(passwordError));
        }

        if (!TryNormalizeRequestRole(request.Role, out var role, out var roleError))
        {
            return Results.BadRequest(new ApiMessageResponse(roleError));
        }

        var created = teacherStore.CreateTeacher(request.Username, request.DisplayName, request.Password, role);
        WriteAdminAudit(audit, httpContext, "admin_teacher_created", admin, created, "ok");
        return Results.Created($"/api/admin/teachers/{Uri.EscapeDataString(created.Username)}", ToAdminTeacherListItem(created));
    }
    catch (InvalidOperationException ex)
    {
        logger.LogWarning(ex, "Admin teacher create request could not be completed.");
        return Results.Conflict(new ApiMessageResponse("Učitelský účet už existuje nebo ho nelze vytvořit."));
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid admin teacher create request.");
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Admin teacher create request failed.");
        return Results.Problem("Učitelský účet se nepodařilo vytvořit.");
    }
});

app.MapPut("/api/admin/teachers/{username}", (string username, AdminUpdateTeacherRequest request, HttpContext httpContext, TeacherAccountStore teacherStore, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeAdmin(httpContext, teacherTokens, audit, out var unauthorizedResult, out var admin))
    {
        return unauthorizedResult;
    }

    try
    {
        if (request is null)
        {
            return Results.BadRequest(new ApiMessageResponse("Požadavek na úpravu učitele není platný."));
        }

        if (request.DisplayName is not null && string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Results.BadRequest(new ApiMessageResponse("Zobrazované jméno nesmí být prázdné."));
        }

        if (!TryNormalizeRequestRole(request.Role, out var role, out var roleError))
        {
            return Results.BadRequest(new ApiMessageResponse(roleError));
        }

        var before = teacherStore.FindTeacher(username);
        if (before is null)
        {
            return Results.NotFound(new ApiMessageResponse("Učitelský účet nebyl nalezen."));
        }

        var updated = teacherStore.UpdateTeacher(username, request.DisplayName, request.Role is null ? null : role);
        WriteAdminAudit(audit, httpContext, "admin_teacher_updated", admin, updated, "ok");
        if (!string.Equals(TeacherRoles.Normalize(before.Role), TeacherRoles.Normalize(updated.Role), StringComparison.Ordinal))
        {
            WriteAdminAudit(audit, httpContext, "admin_teacher_role_changed", admin, updated, "ok");
        }

        return Results.Ok(ToAdminTeacherListItem(updated));
    }
    catch (InvalidOperationException ex) when (IsLastActiveAdminProtection(ex))
    {
        return Results.Conflict(new ApiMessageResponse("Poslední aktivní administrátor musí zůstat aktivní a s rolí Admin."));
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound(new ApiMessageResponse("Učitelský účet nebyl nalezen."));
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid admin teacher update request for {Username}.", username);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Admin teacher update request failed for {Username}.", username);
        return Results.Problem("Učitelský účet se nepodařilo upravit.");
    }
});

app.MapPost("/api/admin/teachers/{username}/reset-password", (string username, AdminResetTeacherPasswordRequest request, HttpContext httpContext, TeacherAccountStore teacherStore, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeAdmin(httpContext, teacherTokens, audit, out var unauthorizedResult, out var admin))
    {
        return unauthorizedResult;
    }

    try
    {
        if (request is null)
        {
            return Results.BadRequest(new ApiMessageResponse("Heslo nesmí být prázdné."));
        }

        if (!TeacherPasswordRules.TryValidate(request.Password, out var passwordError))
        {
            return Results.BadRequest(new ApiMessageResponse(passwordError));
        }

        var changed = teacherStore.SetTeacherPassword(username, request.Password);
        WriteAdminAudit(audit, httpContext, "admin_teacher_password_reset", admin, changed, "ok");
        return Results.Ok(ToAdminTeacherListItem(changed));
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound(new ApiMessageResponse("Učitelský účet nebyl nalezen."));
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid admin teacher password reset request for {Username}.", username);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Admin teacher password reset request failed for {Username}.", username);
        return Results.Problem("Heslo učitele se nepodařilo změnit.");
    }
});

app.MapPost("/api/admin/teachers/{username}/deactivate", (string username, HttpContext httpContext, TeacherAccountStore teacherStore, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeAdmin(httpContext, teacherTokens, audit, out var unauthorizedResult, out var admin))
    {
        return unauthorizedResult;
    }

    try
    {
        var deactivated = teacherStore.SetTeacherActive(username, false);
        WriteAdminAudit(audit, httpContext, "admin_teacher_deactivated", admin, deactivated, "ok");
        return Results.Ok(ToAdminTeacherListItem(deactivated));
    }
    catch (InvalidOperationException ex) when (IsLastActiveAdminProtection(ex))
    {
        return Results.Conflict(new ApiMessageResponse("Poslední aktivní administrátor nesmí být deaktivován."));
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound(new ApiMessageResponse("Učitelský účet nebyl nalezen."));
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid admin teacher deactivate request for {Username}.", username);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Admin teacher deactivate request failed for {Username}.", username);
        return Results.Problem("Učitelský účet se nepodařilo deaktivovat.");
    }
});

app.MapPost("/api/admin/teachers/{username}/activate", (string username, HttpContext httpContext, TeacherAccountStore teacherStore, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeAdmin(httpContext, teacherTokens, audit, out var unauthorizedResult, out var admin))
    {
        return unauthorizedResult;
    }

    try
    {
        var activated = teacherStore.SetTeacherActive(username, true);
        WriteAdminAudit(audit, httpContext, "admin_teacher_activated", admin, activated, "ok");
        return Results.Ok(ToAdminTeacherListItem(activated));
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound(new ApiMessageResponse("Učitelský účet nebyl nalezen."));
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid admin teacher activate request for {Username}.", username);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Admin teacher activate request failed for {Username}.", username);
        return Results.Problem("Učitelský účet se nepodařilo aktivovat.");
    }
});

app.MapDelete("/api/admin/teachers/{username}", (string username, HttpContext httpContext, TeacherAccountStore teacherStore, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeAdmin(httpContext, teacherTokens, audit, out var unauthorizedResult, out var admin))
    {
        return unauthorizedResult;
    }

    try
    {
        var target = teacherStore.FindTeacher(username);
        if (target is null)
        {
            return Results.NotFound(new ApiMessageResponse("Učitelský účet nebyl nalezen."));
        }

        if (string.Equals(username.Trim(), admin.Username, StringComparison.OrdinalIgnoreCase))
        {
            WriteAdminAudit(audit, httpContext, "admin_teacher_deleted", admin, target, "blocked:self-delete", StatusCodes.Status409Conflict);
            return Results.Conflict(new ApiMessageResponse("Právě přihlášený administrátor nemůže odstranit vlastní účet."));
        }

        var deleted = teacherStore.DeleteTeacher(username);
        var revokedSessions = teacherTokens.RevokeTokensForTeacher(deleted.Username);
        WriteAdminAudit(audit, httpContext, "admin_teacher_deleted", admin, deleted, $"ok; revokedSessions={revokedSessions}");
        return Results.Ok(new ApiMessageResponse("Učitelský účet byl odstraněn."));
    }
    catch (InvalidOperationException ex) when (IsLastAdminDeleteProtection(ex))
    {
        var target = teacherStore.FindTeacher(username);
        if (target is not null)
        {
            WriteAdminAudit(audit, httpContext, "admin_teacher_deleted", admin, target, "blocked:last-admin", StatusCodes.Status409Conflict);
        }

        return Results.Conflict(new ApiMessageResponse("Poslední administrátor nesmí být odstraněn."));
    }
    catch (InvalidOperationException ex) when (IsLastActiveAdminProtection(ex))
    {
        var target = teacherStore.FindTeacher(username);
        if (target is not null)
        {
            WriteAdminAudit(audit, httpContext, "admin_teacher_deleted", admin, target, "blocked:last-active-admin", StatusCodes.Status409Conflict);
        }

        return Results.Conflict(new ApiMessageResponse("Poslední aktivní administrátor nesmí být odstraněn."));
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound(new ApiMessageResponse("Učitelský účet nebyl nalezen."));
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid admin teacher delete request for {Username}.", username);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Admin teacher delete request failed for {Username}.", username);
        return Results.Problem("Učitelský účet se nepodařilo odstranit.");
    }
});

app.MapGet("/api/classes/{classId}", (string classId, HttpContext httpContext, IClassDataRepository repository, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeTeacher(httpContext, teacherTokens, audit, out var unauthorizedResult))
    {
        return unauthorizedResult;
    }

    try
    {
        var result = repository.GetClassStudents(classId);
        return Results.Ok(new ClassStudentsResponse(
            classId,
            result.Students,
            result.Success ? null : result.Message));
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid class students request for class {ClassId}.", classId);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
});

app.MapGet("/api/classes/{classId}/overview", (string classId, HttpContext httpContext, IClassDataRepository repository, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeTeacher(httpContext, teacherTokens, audit, out var unauthorizedResult))
    {
        return unauthorizedResult;
    }

    try
    {
        return Results.Ok(new ClassOverviewResponse(classId, repository.GetClassOverview(classId)));
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid class overview request for class {ClassId}.", classId);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Class overview could not be loaded for class {ClassId}.", classId);
        return Results.Problem("Class overview could not be loaded.");
    }
});

app.MapGet("/api/classes/{classId}/activities", (string classId, int? limit, HttpContext httpContext, IClassDataRepository repository, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeTeacher(httpContext, teacherTokens, audit, out var unauthorizedResult))
    {
        return unauthorizedResult;
    }

    try
    {
        var safeLimit = Math.Clamp(limit.GetValueOrDefault(10), 1, 50);
        return Results.Ok(new ClassActivityResponse(classId, repository.GetClassActivities(classId, safeLimit)));
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid class activities request for class {ClassId}.", classId);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Class activities could not be loaded for class {ClassId}.", classId);
        return Results.Problem("Class activities could not be loaded.");
    }
});

app.MapGet("/api/students/{classId}/{studentId}", (string classId, string studentId, HttpContext httpContext, IClassDataRepository repository, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeTeacher(httpContext, teacherTokens, audit, out var unauthorizedResult))
    {
        return unauthorizedResult;
    }

    try
    {
        var student = repository.GetStudent(classId, studentId, out var message);
        return student is null
            ? Results.NotFound(new ApiMessageResponse(message ?? "Student was not found."))
            : Results.Ok(student);
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid student detail request for class {ClassId}, student {StudentId}.", classId, studentId);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
});

app.MapGet("/api/students/{classId}/{studentId}/results", (string classId, string studentId, HttpContext httpContext, IClassDataRepository repository, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeTeacher(httpContext, teacherTokens, audit, out var unauthorizedResult))
    {
        return unauthorizedResult;
    }

    try
    {
        var detail = repository.GetStudentResultDetail(classId, studentId, out var message);
        return detail is null
            ? Results.NotFound(new ApiMessageResponse(message ?? "Student results were not found."))
            : Results.Ok(detail);
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid student results request for class {ClassId}, student {StudentId}.", classId, studentId);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Student results could not be loaded for class {ClassId}, student {StudentId}.", classId, studentId);
        return Results.Problem("Student results could not be loaded.");
    }
});

app.MapPost("/api/classes/{classId}/students", (string classId, CreateStudentRequest requestBody, HttpContext httpContext, IClassDataRepository repository, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeTeacher(httpContext, teacherTokens, audit, out var unauthorizedResult))
    {
        return unauthorizedResult;
    }

    try
    {
        var result = repository.CreateStudent(classId, requestBody);
        return result.Success
            ? Results.Created($"/api/students/{classId}/{result.Student?.StudentId}", result)
            : Results.BadRequest(result);
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid create student request for class {ClassId}.", classId);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Student account could not be created for class {ClassId}.", classId);
        return Results.Problem("Student account could not be created.");
    }
});

app.MapPost("/api/students/{classId}/{studentId}/reset-pin", (string classId, string studentId, HttpContext httpContext, IClassDataRepository repository, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeTeacher(httpContext, teacherTokens, audit, out var unauthorizedResult))
    {
        return unauthorizedResult;
    }

    try
    {
        var result = repository.ResetStudentPin(classId, studentId);
        return result.Success
            ? Results.Ok(result)
            : Results.NotFound(result);
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid student credential reset request for class {ClassId}, student {StudentId}.", classId, studentId);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Student credential reset could not be completed for class {ClassId}, student {StudentId}.", classId, studentId);
        return Results.Problem("Student PIN could not be reset.");
    }
});

app.MapDelete("/api/students/{classId}/{studentId}", (string classId, string studentId, HttpContext httpContext, IClassDataRepository repository, TeacherTokenService teacherTokens, TeacherAuthAuditLogger audit) =>
{
    if (!TryAuthorizeTeacher(httpContext, teacherTokens, audit, out var unauthorizedResult))
    {
        return unauthorizedResult;
    }

    try
    {
        var result = repository.DeleteStudent(classId, studentId);
        return result.Success
            ? Results.Ok(result)
            : Results.NotFound(result);
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid delete student request for class {ClassId}, student {StudentId}.", classId, studentId);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Student could not be deleted for class {ClassId}, student {StudentId}.", classId, studentId);
        return Results.Problem("Student could not be deleted.");
    }
});

app.MapPost("/api/classes/{classId}/login", (string classId, StudentLoginRequest request, HttpContext httpContext, IClassDataRepository repository, StudentLoginLockoutStore studentLoginLockouts, StudentSessionTokenService studentSessionTokens) =>
{
    var safeRequest = request ?? new StudentLoginRequest(string.Empty, string.Empty, string.Empty);
    var remoteAddress = GetRemoteAddress(httpContext);
    var loginCode = safeRequest.LoginCode ?? string.Empty;

    try
    {
        if (studentLoginLockouts.IsLocked(classId, loginCode, remoteAddress, out var retryAfter))
        {
            logger.LogWarning("Student login rejected because lockout is active for class {ClassId} from {RemoteAddress}.", classId, remoteAddress);
            return CreateStudentLoginLockedResult(httpContext, retryAfter);
        }

        var result = repository.LoginStudent(classId, safeRequest);
        if (result.Success)
        {
            studentLoginLockouts.RegisterSuccess(classId, loginCode, remoteAddress);
            var tokenIssue = studentSessionTokens.IssueToken(classId, result.StudentId);
            return Results.Ok(new StudentLoginResult
            {
                Success = result.Success,
                RequiresPinChange = result.RequiresPinChange,
                RequiresStudentConfigurationReload = result.RequiresStudentConfigurationReload,
                Message = result.Message,
                StudentId = result.StudentId,
                DisplayName = result.DisplayName,
                StudentSessionToken = tokenIssue.Token,
                StudentSessionExpiresUtc = tokenIssue.ExpiresUtc
            });
        }

        if (result.RequiresPinChange)
        {
            studentLoginLockouts.RegisterSuccess(classId, loginCode, remoteAddress);
            return Results.Ok(result);
        }

        var failure = studentLoginLockouts.RegisterFailure(classId, loginCode, remoteAddress);
        if (failure.LockoutStarted && failure.LockedUntilUtc.HasValue)
        {
            logger.LogWarning("Student login lockout started for class {ClassId} from {RemoteAddress}.", classId, remoteAddress);
            return CreateStudentLoginLockedResult(httpContext, failure.LockedUntilUtc.Value - DateTime.UtcNow);
        }

        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid login request for class {ClassId}.", classId);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Login could not be completed for class {ClassId}.", classId);
        return Results.Problem("Login could not be completed.");
    }
});

app.MapPost("/api/students/{classId}/{studentId}/results", (string classId, string studentId, StudentSession session, HttpContext httpContext, IClassDataRepository repository, StudentSessionTokenService studentSessionTokens) =>
{
    try
    {
        if (!TryReadBearerToken(httpContext.Request, out var token) ||
            !studentSessionTokens.ValidateToken(token, classId, studentId).Success)
        {
            return CreateStudentAuthorizationRequiredResult();
        }

        var result = repository.SaveStudentResult(classId, studentId, session);
        return Results.Created($"/api/students/{classId}/{studentId}", result);
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid save result request for class {ClassId}, student {StudentId}.", classId, studentId);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Result could not be saved for class {ClassId}, student {StudentId}.", classId, studentId);
        return Results.Problem("Result could not be saved.");
    }
});

app.Run();

static bool TryAuthorizeTeacher(
    HttpContext httpContext,
    TeacherTokenService teacherTokens,
    TeacherAuthAuditLogger audit,
    out IResult? unauthorizedResult)
{
    return TryAuthorizeTeacherRole(
        httpContext,
        teacherTokens,
        audit,
        requiredRole: string.Empty,
        out unauthorizedResult,
        out _);
}

static bool TryAuthorizeAdmin(
    HttpContext httpContext,
    TeacherTokenService teacherTokens,
    TeacherAuthAuditLogger audit,
    out IResult? unauthorizedResult,
    out TeacherTokenValidationResult validation)
{
    return TryAuthorizeTeacherRole(
        httpContext,
        teacherTokens,
        audit,
        TeacherRoles.Admin,
        out unauthorizedResult,
        out validation);
}

static bool TryAuthorizeTeacherRole(
    HttpContext httpContext,
    TeacherTokenService teacherTokens,
    TeacherAuthAuditLogger audit,
    string requiredRole,
    out IResult? unauthorizedResult,
    out TeacherTokenValidationResult validation)
{
    unauthorizedResult = null;
    validation = new TeacherTokenValidationResult(false);
    var request = httpContext.Request;
    var remoteAddress = GetRemoteAddress(httpContext);
    if (!TryReadBearerToken(request, out var token))
    {
        audit.Write("teacher_unauthorized_access", string.Empty, remoteAddress, request.Path, StatusCodes.Status401Unauthorized, "Authorization is missing.");
        unauthorizedResult = Results.Unauthorized();
        return false;
    }

    validation = teacherTokens.ValidateToken(token);
    if (!validation.Success)
    {
        audit.Write("teacher_unauthorized_access", string.Empty, remoteAddress, request.Path, StatusCodes.Status401Unauthorized, validation.Message);
        unauthorizedResult = Results.Unauthorized();
        return false;
    }

    if (string.Equals(requiredRole, TeacherRoles.Admin, StringComparison.Ordinal) &&
        !TeacherRoles.IsAdmin(validation.Role))
    {
        audit.Write("teacher_forbidden_access", validation.Username, remoteAddress, request.Path, StatusCodes.Status403Forbidden, "Admin role is required.", validation.Role);
        unauthorizedResult = Results.StatusCode(StatusCodes.Status403Forbidden);
        return false;
    }

    return true;
}

static bool TryNormalizeRequestRole(string? role, out string normalizedRole, out string errorMessage)
{
    if (TeacherRoles.TryNormalize(role, out normalizedRole))
    {
        errorMessage = string.Empty;
        return true;
    }

    errorMessage = "Role musí být Admin nebo Teacher.";
    return false;
}

static AdminTeacherListItem ToAdminTeacherListItem(TeacherAccount account) =>
    new(
        account.Username,
        account.DisplayName,
        TeacherRoles.Normalize(account.Role),
        account.IsActive,
        account.CreatedUtc,
        account.UpdatedUtc);

static void WriteAdminAudit(
    TeacherAuthAuditLogger audit,
    HttpContext httpContext,
    string eventType,
    TeacherTokenValidationResult admin,
    TeacherAccount target,
    string result,
    int statusCode = StatusCodes.Status200OK)
{
    audit.Write(
        eventType,
        admin.Username,
        GetRemoteAddress(httpContext),
        httpContext.Request.Path,
        statusCode,
        "Admin teacher account action completed.",
        TeacherRoles.Normalize(target.Role),
        target.Username,
        result);
}

static bool IsLastActiveAdminProtection(InvalidOperationException ex) =>
    ex.Message.Contains("last active admin", StringComparison.OrdinalIgnoreCase);

static bool IsLastAdminDeleteProtection(InvalidOperationException ex) =>
    ex.Message.Contains("last admin", StringComparison.OrdinalIgnoreCase);

static IResult CreateTeacherLoginLockedResult(HttpContext httpContext, TimeSpan retryAfter)
{
    var safeRetryAfter = Math.Max(1, (int)Math.Ceiling(Math.Max(0, retryAfter.TotalSeconds)));
    httpContext.Response.Headers.RetryAfter = safeRetryAfter.ToString();
    return Results.Json(
        new ApiMessageResponse("Příliš mnoho pokusů o přihlášení. Zkuste to prosím později."),
        statusCode: StatusCodes.Status423Locked);
}

static IResult CreateStudentLoginLockedResult(HttpContext httpContext, TimeSpan retryAfter)
{
    var safeRetryAfter = Math.Max(1, (int)Math.Ceiling(Math.Max(0, retryAfter.TotalSeconds)));
    httpContext.Response.Headers.RetryAfter = safeRetryAfter.ToString();
    return Results.Json(
        new ApiMessageResponse("Příliš mnoho neúspěšných pokusů. Zkuste to prosím později."),
        statusCode: StatusCodes.Status423Locked);
}

static IResult CreateStudentAuthorizationRequiredResult() =>
    Results.Json(
        new ApiMessageResponse("Přihlášení žáka vypršelo. Přihlas se prosím znovu."),
        statusCode: StatusCodes.Status401Unauthorized);

static string GetRemoteAddress(HttpContext httpContext) =>
    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static bool TryReadBearerToken(HttpRequest request, out string token)
{
    token = string.Empty;
    if (!request.Headers.TryGetValue("Authorization", out var authorizationValues))
    {
        return false;
    }

    var authorization = authorizationValues.ToString();
    const string bearerPrefix = "Bearer ";
    if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    token = authorization[bearerPrefix.Length..].Trim();
    return !string.IsNullOrWhiteSpace(token);
}

static string ResolveDataRoot(string? configuredValue, string contentRootPath)
{
    var value = string.IsNullOrWhiteSpace(configuredValue)
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SchoolMathTrainer",
            "api-data")
        : configuredValue.Trim().Trim('"');

    var expanded = Environment.ExpandEnvironmentVariables(value);
    var fullPath = Path.IsPathRooted(expanded)
        ? expanded
        : Path.Combine(contentRootPath, expanded);

    return Path.GetFullPath(fullPath);
}
