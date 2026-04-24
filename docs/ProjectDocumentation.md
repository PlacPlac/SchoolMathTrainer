# SchoolMathTrainer - projektová dokumentace

Aktualizováno: 2026-04-24

## Přehled

SchoolMathTrainer je desktopové řešení pro školní procvičování matematiky.

Aktuální komponenty:

| Komponenta | Typ | Účel |
|---|---|---|
| `TeacherApp` | Avalonia desktop app | Učitelské GUI pro přihlášení, správu žáků, výsledky, reset PINu a generování `.smtcfg`. |
| `StudentApp` | WPF desktop app | Žákovská aplikace pro import `.smtcfg`, přihlášení, změnu dočasného PINu a odesílání výsledků. |
| `SchoolMathTrainer.Api` | ASP.NET Core API | Produkční a lokální backend pro teacher i student endpointy. |
| `SharedCore` | .NET knihovna | Sdílené modely, validace, statistiky, konfigurace a file-based služby. |
| `SchoolMathTrainer.TeacherAdmin` | Konzolová app | Správa teacher účtů ve file-based storage. |

## Aktuální produkční stav

- Hlavní učitelské GUI je `TeacherApp`.
- Produkční server je `89.221.212.49`.
- Jediný aktuálně povolený klientský `apiBaseUrl` je `http://89.221.212.49`.
- Veřejný provoz jde přes nginx. Kestrel není určený pro přímý veřejný provoz.
- Teacher endpointy vyžadují Bearer token.
- Student login je oddělený flow a po úspěšném přihlášení vydává student session token pro upload výsledků.
- Onboarding žáka probíhá přes `.smtcfg`.

## Lokální vývoj

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
dotnet run --project .\src\SchoolMathTrainer.Api\SchoolMathTrainer.Api.csproj
```

Teacher admin:

```powershell
dotnet run --project .\src\SchoolMathTrainer.TeacherAdmin\SchoolMathTrainer.TeacherAdmin.csproj -- list-teachers --data-root C:\path\to\data
```

Lokální důležité cesty:

```text
%LOCALAPPDATA%\SchoolMathTrainer\api-data
%LOCALAPPDATA%\SchoolMathTrainer\teacher-server-settings.json
%LOCALAPPDATA%\SchoolMathTrainer\shared-data-folder.json
%LOCALAPPDATA%\SchoolMathTrainer\ssh\known_hosts
%LOCALAPPDATA%\SchoolMathTrainer\teacher-sftp-cache\
```

## Server a bezpečnost

- Produkční host: `89.221.212.49`
- Veřejný klientský vstup: `http://89.221.212.49`
- Veřejný port pro klienty: `80`
- Interní API runtime: `127.0.0.1:5078`
- Produkční data root API: `/var/lib/schoolmath/data`
- Security složka: `/var/lib/schoolmath/data/security`
- TeacherApp SSH/SFTP výchozí nastavení: host `89.221.212.49`, port `22`, uživatel `schoolmath`, vzdálená cesta `/srv/schoolmath/data`

Bezpečnostní pravidla:
- Teacher token se drží jen v paměti klienta.
- Server ukládá jen hash teacher session tokenu.
- Teacher login i student login mají lockout ochranu.
- `fail2ban` je součást očekávaného serverového hardeningu.
- Studentský `apiBaseUrl` je striktně omezen na `http://89.221.212.49`.
- Kestrel nesmí být veřejně bindovaný na `0.0.0.0:5078`.

## Onboarding žáka

Aktuální workflow:
1. Učitel se přihlásí v `TeacherApp`.
2. Učitel vybere žáka.
3. Učitel vygeneruje onboarding soubor.
4. `TeacherApp` uloží `<LoginCode>.smtcfg`.
5. `StudentApp` při prvním spuštění vyžádá `.smtcfg`.
6. `StudentApp` import uloží do `%LOCALAPPDATA%\SchoolMathTrainer\shared-data-folder.json`.
7. Žák se přihlásí přes `loginCode` + PIN.
8. Po úspěšném loginu získá student session token pro upload výsledků.

Formát `.smtcfg`:

```json
{
  "version": 1,
  "classId": "production",
  "studentId": "STUDENT_ID",
  "apiBaseUrl": "http://89.221.212.49"
}
```

Soubor `.smtcfg` neobsahuje:
- PIN,
- teacher token,
- teacher credentials,
- SSH klíče,
- jiný host nebo vlastní port.

Validace onboarding souboru:
- `apiBaseUrl` musí být přesně povolená klientská adresa.
- `StudentApp` i backend používají `studentId` z importovaného souboru.
- Pokud `loginCode` patří jinému žákovi než importovaný `studentId`, login se odmítne jako nesoulad konfigurace žáka.

## Autentizace učitele

Primární login endpoint:

```http
POST /api/teacher-auth/login
```

Kompatibilní alias:

```http
POST /api/teachers/login
```

Logout:

```http
POST /api/teachers/logout
```

Teacher autentizace:
- používá username + password,
- vrací opaque session token,
- vyžaduje `Authorization: Bearer <token>` pro teacher endpointy,
- neukládá token do `.smtcfg`,
- neukládá token na disk klienta,
- server ukládá pouze hash tokenu do security evidence.

Teacher endpointy s Bearer tokenem:

| Metoda | Endpoint | Popis |
|---|---|---|
| `GET` | `/api/classes/{classId}` | Seznam žáků. |
| `GET` | `/api/classes/{classId}/overview` | Třídní přehled. |
| `GET` | `/api/classes/{classId}/activities?limit=10` | Poslední aktivity třídy. |
| `GET` | `/api/students/{classId}/{studentId}` | Detail žáka. |
| `GET` | `/api/students/{classId}/{studentId}/results` | Výsledky žáka. |
| `POST` | `/api/classes/{classId}/students` | Vytvoření žáka. |
| `POST` | `/api/students/{classId}/{studentId}/reset-pin` | Reset PINu. |
| `DELETE` | `/api/students/{classId}/{studentId}` | Smazání žáka. |

## Student autentizace

Student login endpoint:

```http
POST /api/classes/{classId}/login
```

Student login používá:
- `loginCode`,
- PIN,
- volitelný `newPin`,
- `studentId` z importovaného `.smtcfg`.

Úspěšný student login vrací:
- `studentId`,
- `displayName`,
- `studentSessionToken`,
- `studentSessionExpiresUtc`.

Upload výsledků:

```http
POST /api/students/{classId}/{studentId}/results
Authorization: Bearer <studentSessionToken>
```

Důležité:
- studentský upload už není anonymní,
- studentský upload nepoužívá teacher token,
- při expiraci student session tokenu musí žák projít znovu login flow,
- backend může vrátit `RequiresPinChange` nebo `RequiresStudentConfigurationReload`.

## Data a úložiště

Projekt používá file-based storage, ne databázi.

Typické produkční cesty:

```text
/var/lib/schoolmath/data
/var/lib/schoolmath/data/production
/var/lib/schoolmath/data/security
```

Lokální a sdílené soubory:
- `teacher-server-settings.json` pro teacher server/SFTP nastavení,
- `shared-data-folder.json` pro importovaný onboarding a lokální přístup žáka,
- `teacher-sftp-cache\` pro read-only cache vzdálených dat,
- `Logs\` pro lokální logy a validační výstupy.

## Zálohy a monitoring

- Repozitář obsahuje jen základní oporu pro logování a audit.
- Teacher auth ukládá audit a lockout data do security složky.
- Lokální validační logy README vznikají ve složce `Logs/`.
- V repozitáři není samostatný automatizační runbook pro pravidelné zálohy.
- Nepopisuj automatické zálohy nebo monitoring jako hotové, pokud nejsou doložené konfigurací nebo provozní dokumentací.

## Zakázané/nepoužívat

- Nepoužívat starý server `89.221.220.226`.
- Nepřenášet `sample-data` ze starého serveru do aktuálního repa.
- Nevystavovat API přímo na `0.0.0.0:5078`.
- Nevkládat tajné údaje do dokumentace ani repozitáře.
- Nevydávat `TeacherDashboard` za hlavní učitelskou aplikaci.
- Nevydávat HTTPS za povinný klientský endpoint, dokud se oficiálně nezmění povolený klientský `apiBaseUrl`.

## Struktura projektu

```text
SchoolMathTrainer.sln
src/
  SchoolMathTrainer.Api/
  SchoolMathTrainer.TeacherAdmin/
  SharedCore/
  StudentApp/
  TeacherApp/
docs/
  ProjectDocumentation.md
sample-data/
tools/
```
