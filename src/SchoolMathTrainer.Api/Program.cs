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
    TeacherTokenService tokenService,
    TeacherAuthAuditLogger audit)
{
    var username = request?.Username ?? string.Empty;
    var password = request?.Password ?? string.Empty;
    var remoteAddress = GetRemoteAddress(httpContext);

    try
    {
        var account = teacherStore.VerifyCredentials(username, password);
        if (account is null)
        {
            audit.Write("teacher_login_failed", username, remoteAddress, httpContext.Request.Path, StatusCodes.Status401Unauthorized, "Invalid teacher credentials.");
            logger.LogWarning("Teacher login failed for username {Username} from {RemoteAddress}.", username, remoteAddress);
            return Results.Unauthorized();
        }

        audit.Write("teacher_login_succeeded", account.Username, remoteAddress, httpContext.Request.Path, StatusCodes.Status200OK, "Teacher credentials accepted.");
        logger.LogInformation("Teacher login succeeded for username {Username}.", account.Username);
        return Results.Ok(tokenService.IssueToken(account));
    }
    catch (ArgumentException ex)
    {
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
        audit.Write("teacher_unauthorized_access", string.Empty, remoteAddress, httpContext.Request.Path, StatusCodes.Status401Unauthorized, "Logout request did not include a bearer token.");
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
        logger.LogWarning(ex, "Invalid reset PIN request for class {ClassId}, student {StudentId}.", classId, studentId);
        return Results.BadRequest(new ApiMessageResponse(ex.Message));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        logger.LogError(ex, "Student PIN could not be reset for class {ClassId}, student {StudentId}.", classId, studentId);
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

app.MapPost("/api/classes/{classId}/login", (string classId, StudentLoginRequest request, IClassDataRepository repository) =>
{
    try
    {
        return Results.Ok(repository.LoginStudent(classId, request));
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

app.MapPost("/api/students/{classId}/{studentId}/results", (string classId, string studentId, StudentSession session, IClassDataRepository repository) =>
{
    try
    {
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
    unauthorizedResult = null;
    var request = httpContext.Request;
    var remoteAddress = GetRemoteAddress(httpContext);
    if (!TryReadBearerToken(request, out var token))
    {
        audit.Write("teacher_unauthorized_access", string.Empty, remoteAddress, request.Path, StatusCodes.Status401Unauthorized, "Bearer token is missing.");
        unauthorizedResult = Results.Unauthorized();
        return false;
    }

    var validation = teacherTokens.ValidateToken(token);
    if (!validation.Success)
    {
        audit.Write("teacher_unauthorized_access", string.Empty, remoteAddress, request.Path, StatusCodes.Status401Unauthorized, validation.Message);
        unauthorizedResult = Results.Unauthorized();
        return false;
    }

    return true;
}

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
