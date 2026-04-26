# AGENTS.md

## Rozsah

Tyto instrukce platí pro celý repozitář od této složky dolů.

## Aktuální realita projektu

Repozitář obsahuje:
- `SchoolMathTrainer.sln`
- `src/TeacherApp`
- `src/StudentApp`
- `src/SchoolMathTrainer.Api`
- `src/SchoolMathTrainer.TeacherAdmin`
- `src/SharedCore`

Hlavní fakta:
- hlavní učitelské GUI je `TeacherApp`,
- produkční server je `89.221.212.49`,
- dočasný produkční klientský endpoint je `http://89.221.212.49`,
- veřejný provoz jde přes nginx,
- API smí na serveru poslouchat jen na `127.0.0.1:5078`,
- veřejné `0.0.0.0:5078` ani externí `:5078` se nesmí používat,
- starý server `89.221.220.226` je kompromitovaný, nepoužitelný a nesmí se používat ani migrovat,
- onboarding žáka probíhá přes `.smtcfg`,
- teacher autentizace používá Bearer token,
- student upload výsledků používá samostatný student session token,
- učitelské role jsou `Admin` a `Teacher`; `/api/admin/*` vyžaduje roli `Admin`.

## Dokumentační pravda

Při úpravách dokumentace drž v souladu hlavně:
- `README.md`
- `AGENTS.md`
- `docs/ProjectDocumentation.md`

Pokud dokumentace tvrdí něco jiného než kód nebo aktuální nasazení, oprav dokumentaci podle kódu a aktuálního nasazení. Do dokumentace nikdy nezapisuj hesla, PINy, tokeny, privátní klíče ani citlivá data.

## StudentApp

`StudentApp` má modernější dětské WPF rozhraní se sytější, veselejší barevnou paletou a kompaktní horní hlavičkou. Před přihlášením zobrazuje načtený žákovský soubor nebo žáka a tlačítko `Změnit žáka` je viditelné a funkční. Po přihlášení funguje bez svislého scrollu, odpovědi v režimech `Začátečník` i `Pokročilý` nejsou dole uříznuté a fungují akce `Nová hra`, `Můj výsledek`, `Třídní výsledky`, `Změnit žáka` a `Zavřít`.

Maskování PINu:
- commit `a900f6c Mask student PIN entry.`,
- PIN je výchoze skrytý,
- `Zobrazit PIN` / `Skrýt PIN` mění jen režim zobrazení, ne hodnotu,
- ruční kontrola: `1234` se po zobrazení ukáže jako `1234`, ne `4321`,
- v režimu změny dočasného PINu se původní dočasný PIN znovu nezobrazuje,
- původní PIN je jen vizuálně označený jako ověřený a aktivní je pole `Nový PIN`,
- nový PIN po dokončení funguje a dočasný PIN už nemá fungovat,
- PIN se nesmí logovat, ukládat do souboru ani zobrazovat v diagnostice.

## Backend a bezpečnost loginu

Redakce API logů:
- commit `3f96ff2 Redact student identifiers from API logs.`,
- auth-related logy nesmí obsahovat konkrétní jména žáků, `studentId`, `loginCode`, PIN, `newPIN`, `PinHash`, `PinSalt`, token, `Bearer` ani jiné citlivé identifikátory,
- používej jen obecné příznaky typu `HasConfiguredStudent`, `AccountFound`, `IsActive`, `success`, `requiresCredentialChange`, `requiresStudentConfigurationReload`.

Student login lockout:
- commit `0ba8cbf Harden student login brute force protection.`,
- stav je v `DataRoot/security/student-login-lockouts.json`,
- lockout klíče jsou hashované,
- student + IP: `classId + loginCode + IP`, 5 pokusů, okno 10 minut, lockout 15 minut,
- student globálně: `classId + loginCode`, 10 pokusů, okno 30 minut, lockout 30 minut,
- class + IP: `classId + IP`, 30 pokusů, okno 10 minut, lockout 30 minut,
- API při lockoutu vrací `423 Locked` a `Retry-After`,
- `StudentApp` zobrazuje `Příliš mnoho neúspěšných pokusů. Zkus to prosím později.`

Invalidní lockout soubor:
- commit `bf5b07d Handle invalid student login lockout files.`,
- `StudentLoginLockoutStore` bezpečně načítá neexistující, prázdný, whitespace-only, `[]`, `{}`, starý slovníkový i poškozený JSON,
- poškozený soubor nesmí shodit login a po dalším zápisu se uloží nový validní formát.

Student session token:
- login vydává `studentSessionToken` a `studentSessionExpiresUtc`,
- token má platnost 8 hodin,
- server ukládá jen hash tokenu v `DataRoot/security/student-sessions.json`,
- `StudentApp` drží token jen v paměti,
- upload výsledků posílá `Authorization: Bearer <student-session-token>`,
- při `401` nebo `403` se token smaže z paměti, žák se odhlásí a výsledek zůstane lokálně.

## TeacherApp a učitelské účty

- `Admin` vidí `Správa učitelů`.
- `Teacher` sekci `Správa učitelů` nevidí.
- Nepřihlášený stav nevidí interní panely.
- Server chrání admin endpointy rolí `Admin`.
- `Teacher` dostává `403`, neplatný nebo nepřihlášený uživatel `401`.
- Nelze odstranit, deaktivovat ani demotovat posledního aktivního `Admina`.
- Nelze odstranit právě přihlášeného `Admina`.
- Odstranění učitele nemaže audit ani žákovská data.
- Odstranění učitele zneplatní jeho sessions.
- Admin API nikdy nevrací hashe, salty, tokeny ani sessions.

Učitelské heslo musí mít alespoň 12 znaků. Přesný text pravidla:

```text
Heslo musí mít alespoň 12 znaků.
```

`username` je technický login bez mezer a diakritiky. Povolené znaky jsou `a-z`, číslice, tečka, pomlčka a podtržítko; maximum je 64 znaků. `displayName` smí obsahovat českou diakritiku.

## Server, monitoring a zálohy

- Aktivní VPS: `89.221.212.49`.
- OS: Ubuntu 24.04.4 LTS.
- Hostname: `schoolmath-server`.
- API služba: `schoolmath-api.service`.
- API běží z `/home/schoolmath/api/SchoolMathTrainer.Api.dll`.
- API poslouchá jen na `127.0.0.1:5078`.
- Nginx je veřejná vstupní brána.
- Dočasný veřejný endpoint: `http://89.221.212.49`.
- DataRoot: `/var/lib/schoolmath/data`.
- Security data: `/var/lib/schoolmath/data/security`.
- `ssh-alert-watcher.sh`, cooldown 3600 sekund.
- `send-telegram-alert.sh`.
- Fail2Ban `sshd`: `maxretry 3`, `findtime 10m`, `bantime 1h`.
- `check-security-updates.timer` denně v 07:30.
- `schoolmath-backup.timer` denně, zálohy do `/var/backups/schoolmath`, retence 7.
- `server-stability-check` každých 5 minut.
- `schoolmath-data-integrity-check` každých 15 minut.

## Úklid projektu

Proběhl bezpečný úklid přes karanténu:
- karanténa `C:\Scripts\Codex\archive\SchoolMathTrainer-cleanup-20260425-223913`,
- audit report `C:\Scripts\Codex\archive\SchoolMathTrainer-cleanup-20260425-223913\cleanup-report.txt`,
- přesunuto 979 souborů/složek,
- karanténu zatím nemazat,
- po úklidu build OK, testy 37/37, git status čistý, `TeacherApp` i `StudentApp` funkční.

## Build a spuštění

TeacherApp:

```powershell
Set-Location 'C:\Scripts\Codex\apps\src\Aplikace_skola_pocitani\SchoolMathTrainer'
dotnet run --project .\src\TeacherApp\TeacherApp.csproj
```

StudentApp:

```powershell
Set-Location 'C:\Scripts\Codex\apps\src\Aplikace_skola_pocitani\SchoolMathTrainer'
dotnet run --project .\src\StudentApp\StudentApp.csproj
```

Ověření:

```powershell
Set-Location 'C:\Scripts\Codex\apps\src\Aplikace_skola_pocitani\SchoolMathTrainer'
$env:GIT_PAGER = 'cat'
dotnet build .\SchoolMathTrainer.sln
dotnet test .\SchoolMathTrainer.sln
git status --short
git log --oneline -10
```

Server:

```bash
systemctl status schoolmath-api.service --no-pager
curl -i http://127.0.0.1:5078/health
curl -i http://89.221.212.49/health
ss -tulpen | grep 5078 || true
dotnet /home/schoolmath/api/SchoolMathTrainer.TeacherAdmin.dll list-teachers --data-root /var/lib/schoolmath/data
```

## Zakázané / nepoužívat

Nikdy:
- nepoužívej starý server `89.221.220.226`,
- nemigruj nic ze starého serveru,
- neprezentuj `TeacherDashboard` jako aktuální učitelskou aplikaci,
- nevystavuj API přímo na `0.0.0.0:5078`,
- nepřipojuj klienty na externí `:5078`,
- nepiš do dokumentace ani repozitáře hesla, PINy, tokeny, privátní klíče ani jiná citlivá data,
- nepřenášej `sample-data` ze starého serveru a nevydávej ji za produkční snapshot.

## Zbývající TODO

- Jednoduchá nástěnka oznámení pro učitele: `Admin` publikuje krátké oznámení a `Teacher` ho po přihlášení vidí; bez chatu, odpovědí, vláken, notifikací a realtime.
- Doména a DNS-based HTTPS.
- Finální release balíček: Windows PowerShell verification procedure, bezpečnostní skeny, SHA-256 hash výstup, Windows code signing `StudentApp.exe`, macOS signing/notarization `TeacherApp.app`, školní schvalovací balíček pro vedení/IT.
