# SchoolMathTrainer

Solution otevres ve Visual Studiu nebo VS Code souborem `SchoolMathTrainer.sln`.

Build:
- `dotnet build SchoolMathTrainer.sln -c Debug`

Nastaveni OneDrive cesty:
- otevri `src/StudentApp/appsettings.json` a `src/TeacherDashboard/appsettings.json`
- nastav `SharedDataRoot` na sdilenou OneDrive slozku
- aplikace si z ni sama odvodí `Data\Students`, `Data\Sessions`, `Data\Exports`, `Config` a `Logs`

Spusteni StudentApp:
- `dotnet run --project src/StudentApp/StudentApp.csproj`

Spusteni TeacherDashboard:
- `dotnet run --project src/TeacherDashboard/TeacherDashboard.csproj`

Ve workspace je pripraven i lokalni vzor sdilene slozky v `sample-data`.
