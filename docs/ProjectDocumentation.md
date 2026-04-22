# SchoolMathTrainer - projektová dokumentace

Aktualizováno: 2026-04-22

## Přehled

SchoolMathTrainer je desktopové řešení pro školní procvičování matematiky. Projekt má učitelskou aplikaci, žákovskou aplikaci a backend API na VPS.

Aktuální učitelské rozhraní je `TeacherApp`.

## Komponenty

| Komponenta | Typ | Účel |
|---|---|---|
| `TeacherApp` | Avalonia desktop app | Přihlášení učitele, správa žáků, výsledky, PIN, generování `.smtcfg`. |
| `StudentApp` | WPF desktop app | Import `.smtcfg`, login žáka, procvičování, upload výsledků. |
| `SchoolMathTrainer.Api` | ASP.NET Core API | Teacher endpointy, student endpointy, file-based data storage. |
| `SharedCore` | .NET knihovna | Sdílené modely, služby, konfigurace, statistiky, storage. |
| `SchoolMathTrainer.TeacherAdmin` | Konzolová app | Správa teacher účtů ve file-based storage. |

## Aktuální stav

- Backend API je stabilní.
- Teacher auth je hotová.
- Teacher endpointy vyžadují Bearer token.
- Student endpointy fungují bez teacher tokenu.
- TeacherApp je funkční: login, načtení žáků, detail, akce, výsledky, moderní layout.
- StudentApp onboarding funguje přes `.smtcfg`.
- Produkční server je `89.221.212.49`.
- API běží interně na `127.0.0.1:5078`.
- Veřejný vstup jde přes nginx na portu `80`.

## Lokální spuštění

Build:

```powershell
dotnet build .\SchoolMathTrainer.sln
```

TeacherApp:

```powershell
dotnet run --project .\src\TeacherApp\TeacherApp.csproj
```

StudentApp:

```powershell
dotnet run --project .\src\StudentApp\StudentApp.csproj
```

API:

```powershell
dotnet run --project .\src\SchoolMathTrainer.Api\SchoolMathTrainer.Api.csproj --urls http://localhost:5078
```

Teacher admin:

```powershell
dotnet run --project .\src\SchoolMathTrainer.TeacherAdmin\SchoolMathTrainer.TeacherAdmin.csproj -- list-teachers --data-root C:\path\to\data
```

## Produkční server

- host: `89.221.212.49`
- veřejná URL: `http://89.221.212.49`
- reverse proxy: nginx
- veřejný port: `80`
- interní API: `http://127.0.0.1:5078`
- runtime složka: `/home/schoolmath/api`
- runtime DLL: `/home/schoolmath/api/SchoolMathTrainer.Api.dll`
- data root: `/var/lib/schoolmath/data`
- produkční třída: `/var/lib/schoolmath/data/production`
- security složka: `/var/lib/schoolmath/data/security`
- systemd služba: `schoolmath-api.service`

Kestrel nemá být přímo veřejně dostupný. Veřejný provoz má jít přes nginx.

## Data storage

Projekt používá file-based storage. Nepoužívá databázi.

Typické produkční cesty:

```text
/var/lib/schoolmath/data
/var/lib/schoolmath/data/production
/var/lib/schoolmath/data/security
```

Lokální konfigurace aplikací je v:

```text
%LOCALAPPDATA%\SchoolMathTrainer
```

Důležité lokální soubory:

- `%LOCALAPPDATA%\SchoolMathTrainer\shared-data-folder.json`
- `%LOCALAPPDATA%\SchoolMathTrainer\teacher-server-settings.json`
- `%LOCALAPPDATA%\SchoolMathTrainer\teacher-sftp-cache\`

## Teacher autentizace

Primární login endpoint:

```http
POST /api/teacher-auth/login
```

Kompatibilní alias:

```http
POST /api/teachers/login
```

Request:

```json
{
  "username": "ucitel",
  "password": "heslo"
}
```

Response:

```json
{
  "token": "opaque-token",
  "expiresUtc": "2026-04-22T12:00:00Z"
}
```

TeacherApp token drží jen v paměti. Token se neukládá na disk a nevkládá se do `.smtcfg`.

Server ukládá pouze hash tokenu do session evidence.

Session storage:

```text
/var/lib/schoolmath/data/security/teacher-sessions.json
```

Teacher requesty používají:

```http
Authorization: Bearer <token>
```

Logout:

```http
POST /api/teachers/logout
```

## Teacher endpointy

Tyto endpointy vyžadují `Authorization: Bearer <token>`:

| Metoda | Endpoint | Popis |
|---|---|---|
| `GET` | `/api/classes/{classId}` | Seznam žáků a safe profily. |
| `GET` | `/api/classes/{classId}/overview` | Třídní přehled. |
| `GET` | `/api/classes/{classId}/activities?limit=10` | Poslední aktivity třídy. |
| `GET` | `/api/students/{classId}/{studentId}` | Detail žáka. |
| `GET` | `/api/students/{classId}/{studentId}/results` | Výsledky žáka. |
| `POST` | `/api/classes/{classId}/students` | Vytvoření žáka. |
| `POST` | `/api/students/{classId}/{studentId}/reset-pin` | Reset PINu. |
| `DELETE` | `/api/students/{classId}/{studentId}` | Smazání žáka. |

Bez tokenu musí teacher endpoint vrátit `401 Unauthorized`.

## Student endpointy

Student endpointy nevyžadují teacher token:

| Metoda | Endpoint | Popis |
|---|---|---|
| `POST` | `/api/classes/{classId}/login` | Login žáka přes `loginCode` + PIN. |
| `POST` | `/api/students/{classId}/{studentId}/results` | Upload výsledků žáka. |

To je záměr kvůli StudentApp workflow a onboarding souboru.

## Onboarding žáka

1. Učitel se přihlásí v `TeacherApp`.
2. Učitel načte data ze serveru.
3. Učitel vybere žáka.
4. Učitel vygeneruje soubor pro žáka.
5. TeacherApp vytvoří `.smtcfg`.
6. Žák spustí `StudentApp`.
7. StudentApp při prvním spuštění vyžádá `.smtcfg`.
8. StudentApp soubor importuje.
9. StudentApp uloží konfiguraci lokálně.
10. Žák se přihlásí pomocí `loginCode` + PIN.
11. StudentApp odesílá výsledky na server.

Formát `.smtcfg`:

```json
{
  "version": 1,
  "classId": "production",
  "studentId": "STUDENT_ID",
  "apiBaseUrl": "http://89.221.212.49"
}
```

Soubor neobsahuje:

- PIN
- teacher token
- teacher credentials

## TeacherApp

TeacherApp je jediné učitelské GUI.

Funkce:

- login učitele
- logout učitele
- načtení žáků ze serveru
- výběr žáka
- detail žáka
- výsledky žáka
- třídní přehled
- reset PINu
- smazání žáka
- vytvoření žáka
- generování `.smtcfg`

Lokální spuštění:

```powershell
dotnet run --project .\src\TeacherApp\TeacherApp.csproj
```

## StudentApp

StudentApp je žákovská aplikace.

Funkce:

- import `.smtcfg` při prvním spuštění
- přihlášení přes `loginCode` + PIN
- vynucení změny dočasného PINu
- procvičování
- odeslání výsledků na server

Lokální spuštění:

```powershell
dotnet run --project .\src\StudentApp\StudentApp.csproj
```

## Struktura projektu

```text
SchoolMathTrainer.sln
src/
  SchoolMathTrainer.Api/
    Program.cs
    Services/
  SchoolMathTrainer.TeacherAdmin/
  SharedCore/
    Models/
    Services/
  StudentApp/
    Views/
    ViewModels/
    Services/
  TeacherApp/
    MainWindow.axaml
    MainWindow.axaml.cs
    Data/
    Settings/
docs/
  ProjectDocumentation.md
sample-data/
```

## Bezpečnost

Aktuální stav:

- API běží interně na `127.0.0.1:5078`.
- Veřejný vstup jde přes nginx.
- SSH je přes klíč.
- `fail2ban` je aktivní.
- Teacher endpointy vyžadují Bearer token.
- Server ukládá pouze hash teacher tokenu.
- Student endpointy nepoužívají teacher token.
- `.smtcfg` neobsahuje teacher token ani PIN.

Ještě připravit:

- HTTPS
- nginx rate limiting
- pravidelné zálohy
- alert tuning
- admin role pro správu učitelů

## Build a distribuce

Build:

```powershell
dotnet build .\SchoolMathTrainer.sln
```

Budoucí distribuční úkoly:

- Windows build pro `StudentApp.exe`
- Windows build pro `TeacherApp.exe`
- code signing
- macOS balení `TeacherApp.app`
- macOS notarizace

## Testovací checklist

Po změnách ověř:

1. Build celé solution projde.
2. TeacherApp se spustí.
3. Učitel se přihlásí.
4. Teacher endpoint bez tokenu vrací `401`.
5. Teacher endpoint s tokenem vrací `200`.
6. TeacherApp načte seznam žáků.
7. Kliknutí na žáka zobrazí detail.
8. Reset PINu funguje.
9. Smazání testovacího žáka funguje.
10. Generování `.smtcfg` funguje.
11. StudentApp při prvním spuštění importuje `.smtcfg`.
12. Student login funguje bez teacher tokenu.
13. Student upload výsledků funguje bez teacher tokenu.

## Užitečné příkazy

Build:

```powershell
dotnet build .\SchoolMathTrainer.sln
```

TeacherApp:

```powershell
dotnet run --project .\src\TeacherApp\TeacherApp.csproj
```

StudentApp:

```powershell
dotnet run --project .\src\StudentApp\StudentApp.csproj
```

API lokálně:

```powershell
dotnet run --project .\src\SchoolMathTrainer.Api\SchoolMathTrainer.Api.csproj --urls http://localhost:5078
```

List teacher účtů na serveru:

```bash
/usr/bin/dotnet /home/schoolmath/api/SchoolMathTrainer.TeacherAdmin.dll list-teachers --data-root /var/lib/schoolmath/data
```

Nastavení hesla existujícího teacher účtu:

```bash
/usr/bin/dotnet /home/schoolmath/api/SchoolMathTrainer.TeacherAdmin.dll set-teacher-password --username ucitel --password "TestUcitel123!" --data-root /var/lib/schoolmath/data
```

Teacher login test na serveru:

```bash
curl -i -X POST http://127.0.0.1/api/teacher-auth/login -H "Content-Type: application/json" -d '{"username":"ucitel","password":"TestUcitel123!"}'
```

Teacher endpoint bez tokenu:

```bash
curl -i http://127.0.0.1/api/classes/production
```

Teacher endpoint s tokenem:

```bash
curl -i http://127.0.0.1/api/classes/production -H "Authorization: Bearer <token>"
```

## TODO / roadmap

- nginx rate limiting
- pravidelný backup
- HTTPS
- alert tuning
- Cockpit
- admin role pro správu učitelů
- code signing
- macOS notarizace
