using SharedCore.Models;

namespace SchoolMathTrainer.Api.Services;

internal interface IClassDataRepository
{
    ClassDataReadResult GetClassStudents(string classId);
    IReadOnlyList<ClassOverviewItem> GetClassOverview(string classId);
    StudentResultDetailResponse? GetStudentResultDetail(string classId, string studentId, out string? message);
    IReadOnlyList<ClassActivityItemResponse> GetClassActivities(string classId, int limit);
    StudentProfileResponse? GetStudent(string classId, string studentId, out string? message);
    TeacherStudentChangeResponse CreateStudent(string classId, CreateStudentRequest request);
    TeacherStudentChangeResponse ResetStudentPin(string classId, string studentId);
    TeacherStudentChangeResponse DeleteStudent(string classId, string studentId);
    StudentLoginResult LoginStudent(string classId, StudentLoginRequest request);
    SaveStudentResultResponse SaveStudentResult(string classId, string studentId, StudentSession session);
}
