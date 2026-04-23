# SchoolMathTrainer

## Popis projektu

SchoolMathTrainer je desktopové řešení pro procvičování matematiky ve škole.

Slouží pro:

- učitele: správa žáků, reset PINu, mazání žáků, přehled výsledků, generování konfiguračního souboru pro žáka
- žáky: přihlášení pomocí `loginCode` + PIN, procvičování, odesílání výsledků na server

Aktuální učitelské rozhraní je `TeacherApp`.

## Architektura

- `TeacherApp`: Avalonia desktop aplikace pro učitele.
- `StudentApp`: WPF desktop aplikace pro žáka.
- `SchoolMathTrainer.Api`: ASP.NET Core backend na VPS.
- `SharedCore`: sdílené modely, služby, konfigurace, výpočty statistik a file-based storage.
- `SchoolMathTrainer.TeacherAdmin`: konzolový nástroj pro správu teacher účtů.
- Data storage: file-based JSON data, bez databáze.

## Aktuální stav

- backend: hotovo
- teacher autentizace: hotovo
- student workflow: hotovo
- TeacherApp UI: funkční po úpravách layoutu
- StudentApp onboarding: funkční přes konfigurační soubor `.smtcfg`
- produkční server: aktivní server `89.221.212.49`

## Spuštění projektu lokálně

Build celé solution:

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

Teacher admin:

```powershell
dotnet run --project .\src\SchoolMathTrainer.TeacherAdmin\SchoolMathTrainer.TeacherAdmin.csproj -- list-teachers --data-root C:\path\to\data
```

## Server produkce

- VPS: `89.221.212.49`
- klientský veřejný vstup: `http://89.221.212.49`
- nginx reverse proxy: ano
- API runtime: `127.0.0.1:5078`
- klientský veřejný přístup: port `80`
- serverové HTTPS je připravené na `https://89.221.212.49` a portu `443`, ale klientská IP-based TLS kompatibilita ve Windows zatím není spolehlivá, proto klienti dočasně používají HTTP.
- API data root: `/var/lib/schoolmath/data`
- produkční třída: `/var/lib/schoolmath/data/production`
- security data: `/var/lib/schoolmath/data/security`

API není přímo vystavené do internetu na Kestrel portu. Veřejný provoz jde přes nginx.

## Autentizace

### Učitel

Teacher login:

```http
POST /api/teacher-auth/login
```

Kompatibilní alias:

```http
POST /api/teachers/login
```

Úspěšná odpověď obsahuje:

```json
{
  "token": "string",
  "expiresUtc": "2026-04-22T12:00:00Z"
}
```

Teacher endpointy vyžadují:

```http
Authorization: Bearer <token>
```

Token je opaque server-side session token. Klient drží token jen v paměti. Server ukládá pouze hash tokenu do file-based session storage.

Logout:

```http
POST /api/teachers/logout
```

### Žák

Student login používá:

- `loginCode`
- PIN
- `studentId` z importovaného `.smtcfg`

Student endpointy nevyžadují teacher Bearer token.

## Onboarding žáka

Onboarding workflow:

1. Učitel otevře `TeacherApp`.
2. Učitel se přihlásí.
3. Učitel načte žáky ze serveru.
4. Učitel vybere žáka.
5. Učitel klikne na `Vygenerovat soubor pro žáka`.
6. TeacherApp uloží soubor `<LoginCode>.smtcfg`.
7. Žák spustí `StudentApp`.
8. StudentApp při prvním spuštění vyžádá konfigurační soubor.
9. Žák vybere `.smtcfg`.
10. StudentApp uloží konfiguraci do lokálního profilu.
11. StudentApp se připojí na server.
12. Žák se přihlásí pomocí `loginCode` + PIN.

Soubor `.smtcfg` obsahuje:

```json
{
  "version": 1,
  "classId": "production",
  "studentId": "STUDENT_ID",
  "apiBaseUrl": "http://89.221.212.49"
}
```

Soubor `.smtcfg` neobsahuje PIN a neobsahuje teacher token.

## Struktura projektu

```text
SchoolMathTrainer.sln
src/
  SchoolMathTrainer.Api/          Backend API
  SchoolMathTrainer.TeacherAdmin/ Konzolová správa teacher účtů
  SharedCore/                     Sdílené modely a služby
  StudentApp/                     Desktop aplikace pro žáka
  TeacherApp/                     Desktop aplikace pro učitele
docs/
  ProjectDocumentation.md         Podrobnější projektová dokumentace
sample-data/                      Lokální vzorová file-based data
```

## Bezpečnost

- API není veřejně vystavené přímo; Kestrel běží na `127.0.0.1:5078`.
- Klientský veřejný HTTP vstup jde dočasně přes nginx reverse proxy.
- Serverové HTTPS zůstává nasazené, ale klientská IP-based TLS kompatibilita ve Windows zatím není spolehlivá.
- Teacher endpointy jsou chráněné Bearer tokenem.
- Teacher token je server-side session token; server ukládá pouze hash tokenu.
- Teacher token se neukládá do student konfigurace.
- Student endpointy zůstávají bez teacher tokenu kvůli StudentApp workflow.
- SSH přístup na server je klíčem.
- `fail2ban` je aktivní.

## Build a distribuce

Aktuální build:

```powershell
dotnet build .\SchoolMathTrainer.sln
```

## Obnova na novém PC

```powershell
git clone <repo-url>
cd SchoolMathTrainer
.\tools\install-git-hooks.ps1
dotnet build .\SchoolMathTrainer.sln
```

Po buildu lze pokračovat spuštěním `TeacherApp` nebo `StudentApp` příkazy ze sekce `Spuštění projektu lokálně`.

Privátní SSH klíče, tokeny, hesla a lokální secrets nejsou součástí repozitáře. Serverové SSH klíče je nutné obnovit mimo Git.

Připravené směry distribuce:

- Windows: `StudentApp.exe`
- Windows: `TeacherApp.exe`
- macOS: budoucí balení `TeacherApp.app`
- code signing: připravit
- macOS notarizace: připravit

## Testování

Po spuštění ověř:

1. TeacherApp se spustí.
2. Učitel se přihlásí přes username + password.
3. TeacherApp načte žáky ze serveru.
4. Kliknutí na žáka zobrazí detail vpravo.
5. Akce `Resetovat PIN` funguje pro vybraného žáka.
6. Akce `Smazat žáka` funguje pro vybraného žáka.
7. Akce `Vygenerovat soubor pro žáka` vytvoří `.smtcfg`.
8. StudentApp při prvním spuštění importuje `.smtcfg`.
9. Student login funguje bez teacher tokenu.
10. Odeslání výsledků ze StudentApp funguje bez teacher tokenu.

## TODO / roadmap

- rate limiting v nginx
- pravidelný backup dat
- HTTPS na serveru: hotovo, klientská produkční URL je dočasně HTTP
- alert tuning
- Cockpit
- admin role pro správu učitelů
- code signing pro Windows buildy
- notarizace pro macOS buildy
