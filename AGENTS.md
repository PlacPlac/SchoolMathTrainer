# AGENTS.md

## Rozsah

Tyto instrukce platí pro celý repozitář od této složky dolů.

## Aktuální realita projektu

Repozitář dnes obsahuje:
- `SchoolMathTrainer.sln`
- `src/TeacherApp`
- `src/StudentApp`
- `src/SchoolMathTrainer.Api`
- `src/SchoolMathTrainer.TeacherAdmin`
- `src/SharedCore`

Aktuální hlavní fakta:
- hlavní učitelské GUI je `TeacherApp`,
- produkční server je `89.221.212.49`,
- povolený klientský `apiBaseUrl` je `http://89.221.212.49`,
- veřejný provoz jde přes nginx,
- interní API runtime běží na `127.0.0.1:5078`,
- onboarding žáka probíhá přes `.smtcfg`,
- teacher autentizace používá Bearer token,
- učitelské role jsou `Admin` a `Teacher`; `/api/admin/*` vyžaduje roli `Admin`,
- poslední aktivní `Admin` nesmí být deaktivován ani převeden na `Teacher`,
- učitelský účet lze odstranit jen přes admin endpoint; nelze odstranit posledního `Admin` ani právě přihlášeného administrátora,
- `TeacherApp` před přihlášením učitele zobrazuje jen přihlašovací část a bezpečný stav; interní administrace, seznamy, výsledky a akce se žáky se zobrazí až po úspěšné autentizaci a po odhlášení nebo ztrátě session se znovu skryjí,
- `TeacherApp` po přihlášení s rolí `Admin` zobrazuje sekci `Správa učitelů`; běžný `Teacher` ji nevidí,
- studentský upload výsledků používá student session token,
- `StudentApp` má modernizované dětské WPF UI s barevnější pastelovou paletou, kompaktní horní hlavičkou, viditelným načteným žákovským souborem před přihlášením, dostupným tlačítkem `Změnit žáka` před přihlášením a sekcemi `Vyber si procvičování` a `Výsledky`.

## Dokumentační pravda

Při úpravách dokumentace drž v souladu hlavně tyto soubory:
- `README.md`
- `docs/ProjectDocumentation.md`
- `Logs/README.txt`
- `sample-data/README.txt`
- `sample-data/Config/README.txt`
- `sample-data/Logs/README.txt`

Pokud dokumentace tvrdí něco jiného než kód, oprav dokumentaci podle kódu a aktuálního nasazení.

## Build a spuštění

Build celé solution:

```powershell
dotnet build .\SchoolMathTrainer.sln
```

Spuštění `TeacherApp`:

```powershell
dotnet run --project .\src\TeacherApp\TeacherApp.csproj
```

Spuštění `StudentApp`:

```powershell
dotnet run --project .\src\StudentApp\StudentApp.csproj
```

Spuštění API:

```powershell
dotnet run --project .\src\SchoolMathTrainer.Api\SchoolMathTrainer.Api.csproj
```

Teacher admin:

```powershell
dotnet run --project .\src\SchoolMathTrainer.TeacherAdmin\SchoolMathTrainer.TeacherAdmin.csproj -- list-teachers --data-root C:\path\to\data
```

## Bezpečnostní pravidla

Nikdy:
- nepoužívej starý server `89.221.220.226`, protože je kompromitovaný,
- neprezentuj `TeacherDashboard` jako aktuální učitelskou aplikaci,
- nevystavuj API přímo na `0.0.0.0:5078`,
- nepiš do dokumentace ani repozitáře hesla, tokeny, privátní klíče ani jiné tajné údaje,
- nepřenášej `sample-data` ze starého serveru a nevydávej ji za produkční snapshot,
- neoznačuj HTTPS za povinný klientský endpoint, dokud se nezmění aktuální povolený `apiBaseUrl`.

Vždy:
- udržuj dokumentaci v souladu s kódem,
- uváděj `TeacherApp` jako hlavní učitelskou aplikaci,
- rozlišuj teacher token a student session token,
- uváděj, že veřejný klientský vstup je přes nginx a interní API běží na `127.0.0.1:5078`.

## Onboarding a autentizace

Onboarding:
- `TeacherApp` generuje `.smtcfg`,
- `.smtcfg` obsahuje `version`, `classId`, `studentId`, `apiBaseUrl`,
- `.smtcfg` neobsahuje PIN ani teacher token.

Teacher autentizace:
- `POST /api/teacher-auth/login`
- alias `POST /api/teachers/login`
- teacher endpointy vyžadují `Authorization: Bearer <token>`
- logout je `POST /api/teachers/logout`
- učitelská data se bez přihlášení nenačítají a interní panely `TeacherApp` se před přihlášením nezobrazují
- admin endpointy `/api/admin/teachers*` vyžadují bearer token s rolí `Admin`
- `DELETE /api/admin/teachers/{username}` nemaže audit ani žákovská data a pro běžné vypnutí účtu je vhodnější deaktivace
- admin UI v `TeacherApp` používá server-side chráněné endpointy pro přidání, úpravu, změnu role, reset hesla, aktivaci, deaktivaci a odstranění učitele; hesla se nezapisují do příkazů ani dokumentace
- první admin účet se vytváří přes `TeacherAdmin` CLI s `--role Admin`; heslo se zadává interaktivně a nesmí být v příkazu

Student autentizace:
- `POST /api/classes/{classId}/login`
- po úspěchu vzniká `studentSessionToken`
- token se drží jen v paměti `StudentApp` a neukládá se do `.smtcfg` ani na disk klienta
- upload výsledků na `POST /api/students/{classId}/{studentId}/results` vyžaduje student Bearer token
- při `401` nebo `403` se žák odhlásí a výsledek zůstane lokálně
