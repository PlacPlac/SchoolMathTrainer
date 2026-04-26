# SchoolMathTrainer - projektová dokumentace

Aktualizováno: 2026-04-26

## Přehled

SchoolMathTrainer je desktopové řešení pro školní procvičování matematiky.

| Komponenta | Typ | Účel |
|---|---|---|
| `TeacherApp` | Avalonia desktop app | Učitelské GUI pro přihlášení, správu žáků, výsledky, reset PINu, generování `.smtcfg` a admin správu učitelů. |
| `StudentApp` | WPF desktop app | Žákovská aplikace pro import `.smtcfg`, přihlášení, změnu dočasného PINu, procvičování a upload výsledků. |
| `SchoolMathTrainer.Api` | ASP.NET Core API | Produkční a lokální backend pro teacher i student endpointy. |
| `SharedCore` | .NET knihovna | Sdílené modely, validace, statistiky, konfigurace a file-based služby. |
| `SchoolMathTrainer.TeacherAdmin` | Konzolová app | Správa teacher účtů ve file-based storage. |

## StudentApp UI

`StudentApp` má modernější dětské rozhraní se sytější, veselejší barevnou paletou, kompaktní horní hlavičkou a herní obrazovkou bez svislého scrollu po přihlášení. Odpovědi v režimu `Začátečník` i `Pokročilý` nejsou dole uříznuté.

Před přihlášením:
- zobrazuje načtený žákovský soubor nebo žáka,
- tlačítko `Změnit žáka` je viditelné a funkční,
- login/session/upload logika je zachovaná.

Po přihlášení funguje:
- `Začátečník`,
- `Pokročilý`,
- `Nová hra`,
- `Můj výsledek`,
- `Třídní výsledky`,
- `Změnit žáka`,
- `Zavřít`.

### Maskování PINu

Commit: `a900f6c Mask student PIN entry.`

PIN je při zadávání standardně skrytý. Přepínač `Zobrazit PIN` / `Skrýt PIN` funguje pro běžné přihlášení a mění pouze režim zobrazení, ne hodnotu. Ruční ověření musí potvrdit, že při zadání `1234` se po zobrazení ukáže přesně `1234`, ne `4321`.

Při změně dočasného PINu:
- původní dočasný PIN se znovu nezobrazuje,
- původní PIN je pouze vizuálně označený jako ověřený, například maskou `****`,
- aktivní je pole `Nový PIN`,
- přepínač smí zobrazit skutečnou hodnotu pouze u pole `Nový PIN`,
- nový PIN po dokončení funguje,
- dočasný PIN po změně už nemá fungovat.

PIN, nový PIN ani dočasný PIN se nesmí logovat, ukládat do souboru ani zobrazovat v diagnostice.

## TeacherApp UI a admin správa

`TeacherApp` má oddělený nepřihlášený a přihlášený stav. Nepřihlášený stav nevidí interní panely. Po přihlášení běžný `Teacher` používá učitelské rozhraní bez sekce `Správa učitelů`. `Admin` vidí navíc sekci `Správa učitelů`.

Server chrání admin endpointy rolí `Admin`. `Teacher` dostává `403`, neplatný nebo nepřihlášený uživatel `401`.

Admin pravidla:
- nelze odstranit, deaktivovat ani demotovat posledního aktivního `Admina`,
- nelze odstranit právě přihlášeného `Admina`,
- odstranění učitele nemaže audit ani žákovská data,
- odstranění učitele zneplatní jeho sessions,
- admin API nikdy nevrací hashe, salty, tokeny ani sessions.

Učitelské heslo musí mít alespoň 12 znaků. Přesný text pravidla:

```text
Heslo musí mít alespoň 12 znaků.
```

`username` je technický login bez mezer a diakritiky. Povolené znaky jsou `a-z`, číslice, tečka, pomlčka a podtržítko; maximální délka je 64 znaků. `displayName` smí obsahovat českou diakritiku.

## Backend - redakce auth logů

Commit: `3f96ff2 Redact student identifiers from API logs.`

Serverové auth-related logy nesmí obsahovat:
- konkrétní jména žáků,
- `studentId`,
- `loginCode`,
- PIN,
- `newPIN`,
- `PinHash`,
- `PinSalt`,
- token,
- `Bearer`,
- jiné citlivé identifikátory.

Logování má používat jen obecné příznaky typu:
- `HasConfiguredStudent`,
- `AccountFound`,
- `IsActive`,
- `success`,
- `requiresCredentialChange`,
- `requiresStudentConfigurationReload`.

Klientské diagnostické logy ve `StudentApp` a `TeacherApp` také nesmí zapisovat konkrétní `studentId`, session id ani potenciálně citlivé texty výsledků; používají jen obecné příznaky jako `HasStudentId`, `HasConfiguredStudent`, `HasMessage`, HTTP status, class a typ operace.

## Student autentizace a session token

Student login endpoint:

```http
POST /api/classes/{classId}/login
```

Login vydává `studentSessionToken` a `studentSessionExpiresUtc`. Token má platnost 8 hodin. Server ukládá jen hash tokenu do:

```text
DataRoot/security/student-sessions.json
```

`StudentApp` drží token jen v paměti. Upload výsledků používá:

```http
Authorization: Bearer <student-session-token>
```

Při `401` nebo `403` `StudentApp` smaže token z paměti, odhlásí žáka a výsledek nechá lokálně.

## Student login lockout

Commit: `0ba8cbf Harden student login brute force protection.`

Lockout je file-based, používá hashované klíče a stav ukládá do:

```text
DataRoot/security/student-login-lockouts.json
```

Ochranné vrstvy:

| Vrstva | Klíč | Limit | Okno | Lockout |
|---|---|---:|---|---|
| student + IP | `classId + loginCode + IP` | 5 pokusů | 10 minut | 15 minut |
| student globálně | `classId + loginCode` | 10 pokusů | 30 minut | 30 minut |
| class + IP | `classId + IP` | 30 pokusů | 10 minut | 30 minut |

API při lockoutu vrací `423 Locked` a `Retry-After`. Pokud narazí více vrstev současně, používá se nejdelší zbývající lockout. `StudentApp` zobrazuje:

```text
Příliš mnoho neúspěšných pokusů. Zkus to prosím později.
```

Commit: `bf5b07d Handle invalid student login lockout files.`

`StudentLoginLockoutStore` bezpečně načítá:
- neexistující soubor,
- prázdný soubor,
- whitespace-only soubor,
- `[]`,
- `{}`,
- starý slovníkový formát,
- poškozený JSON.

Poškozený lockout soubor nesmí shodit login. Po dalším zápisu se uloží nový validní formát.

## Onboarding žáka

Aktuální workflow:
1. Učitel se přihlásí v `TeacherApp`.
2. Učitel vybere žáka.
3. Učitel vygeneruje onboarding soubor.
4. `TeacherApp` uloží `<LoginCode>.smtcfg`.
5. `StudentApp` při prvním spuštění vyžádá `.smtcfg`.
6. `StudentApp` import uloží do lokální konfigurace.
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

Soubor `.smtcfg` neobsahuje PIN, teacher token, teacher credentials, SSH klíče ani vlastní port.

## Serverový stav

- Aktivní VPS: `89.221.212.49`.
- OS: Ubuntu 24.04.4 LTS.
- Hostname: `schoolmath-server`.
- API služba: `schoolmath-api.service`.
- API běží z `/home/schoolmath/api/SchoolMathTrainer.Api.dll`.
- API poslouchá jen na `127.0.0.1:5078`.
- Nginx je veřejná vstupní brána.
- Dočasný veřejný endpoint: `http://89.221.212.49`.
- Port `5078` se zvenku nepoužívá.
- Dlouhodobě: doména + Let’s Encrypt TLS.
- Starý server `89.221.220.226` je kompromitovaný a nesmí se používat ani migrovat.

## Monitoring a zálohy

- `ssh-alert-watcher.sh`, cooldown 3600 sekund.
- `send-telegram-alert.sh`.
- Fail2Ban `sshd`: `maxretry 3`, `findtime 10m`, `bantime 1h`.
- `check-security-updates.timer` denně v 07:30.
- `schoolmath-backup.timer` denně, zálohy do `/var/backups/schoolmath`, retence 7.
- `server-stability-check` každých 5 minut.
- `schoolmath-data-integrity-check` každých 15 minut.
- DataRoot: `/var/lib/schoolmath/data`.
- Security data: `/var/lib/schoolmath/data/security`.

## Úklid projektu

Proběhl bezpečný úklid přes karanténu.

- Karanténa: `C:\Scripts\Codex\archive\SchoolMathTrainer-cleanup-20260425-223913`.
- Audit report: `C:\Scripts\Codex\archive\SchoolMathTrainer-cleanup-20260425-223913\cleanup-report.txt`.
- Přesunuto 979 souborů/složek.
- Karanténu zatím nemazat.
- Po úklidu build OK, testy 37/37, git status čistý, `TeacherApp` i `StudentApp` funkční.

## Spouštěcí příkazy

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

## Ověřovací příkazy

PowerShell:

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

- Nepoužívat starý server `89.221.220.226`; je kompromitovaný a nesmí se používat ani migrovat.
- Nepřenášet `sample-data` ze starého serveru do aktuálního repozitáře.
- Nevystavovat API přímo na `0.0.0.0:5078`.
- Nepřipojovat klienty na externí port `5078`.
- Nevkládat hesla, PINy, tokeny, privátní klíče ani citlivá data do dokumentace nebo repozitáře.
- Nevydávat `TeacherDashboard` za hlavní učitelskou aplikaci.

## Zbývající TODO

- Jednoduchá nástěnka oznámení pro učitele: `Admin` publikuje krátké oznámení a `Teacher` ho po přihlášení vidí; bez chatu, odpovědí, vláken, notifikací a realtime.
- Doména a DNS-based HTTPS.
- Finální release balíček:
  - Windows PowerShell verification procedure,
  - bezpečnostní skeny,
  - SHA-256 hash výstup,
  - Windows code signing `StudentApp.exe`,
  - macOS signing/notarization `TeacherApp.app`,
  - školní schvalovací balíček pro vedení/IT.

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
