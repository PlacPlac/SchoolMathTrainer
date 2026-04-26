# SchoolMathTrainer

SchoolMathTrainer je desktopové řešení pro školní procvičování matematiky.

Aktuální části projektu:
- `TeacherApp`: učitelská desktop aplikace v Avalonia UI.
- `StudentApp`: žákovská desktop aplikace ve WPF.
- `SchoolMathTrainer.Api`: ASP.NET Core backend.
- `SharedCore`: sdílené modely a služby.
- `SchoolMathTrainer.TeacherAdmin`: konzolový nástroj pro správu učitelských účtů.

## Aktuální produkční stav

- Aktivní VPS je `89.221.212.49`.
- Dočasný produkční klientský endpoint je `http://89.221.212.49`.
- API smí na serveru poslouchat pouze lokálně na `http://127.0.0.1:5078` za nginxem.
- Veřejné `0.0.0.0:5078` ani externí přístup na port `5078` se nesmí používat.
- Starý server `89.221.220.226` je kompromitovaný, nepoužitelný a nesmí se používat ani migrovat.
- Dlouhodobý cíl je doména a Let’s Encrypt TLS.

## StudentApp

`StudentApp` má modernější dětské rozhraní se sytější, veselejší barevnou paletou a kompaktní horní hlavičkou. Před přihlášením zobrazuje načtený žákovský soubor nebo žáka a tlačítko `Změnit žáka` je viditelné a funkční už před přihlášením.

Po přihlášení funguje bez svislého scrollu na běžném desktopovém rozlišení. Odpovědi v režimech `Začátečník` i `Pokročilý` nejsou dole uříznuté. Zachované akce: `Začátečník`, `Pokročilý`, `Nová hra`, `Můj výsledek`, `Třídní výsledky`, `Změnit žáka`, `Zavřít`. Login, session a upload logika zůstává zachovaná.

### Maskování PINu

Commit: `a900f6c Mask student PIN entry.`

- PIN je při zadávání standardně skrytý.
- Přepínač `Zobrazit PIN` / `Skrýt PIN` funguje pro běžné přihlášení.
- Ruční kontrola: při zadání `1234` se po zobrazení musí ukázat `1234`, ne `4321`.
- Při změně dočasného PINu se původní dočasný PIN znovu nezobrazuje.
- Původní PIN je v režimu změny PINu jen vizuálně označený jako ověřený, například maskou `****`.
- Aktivní je pole `Nový PIN`.
- Přepínač v režimu změny PINu smí odkrýt jen hodnotu pole `Nový PIN`, nikdy původní dočasný PIN.
- Nový PIN po dokončení funguje a dočasný PIN už po změně fungovat nemá.
- PIN se nesmí logovat, ukládat do souboru ani zobrazovat v diagnostice.

## Student login a upload výsledků

Student login vydává `studentSessionToken` a `studentSessionExpiresUtc`. Token má platnost 8 hodin, server ukládá jen jeho hash a stav je uložený v `DataRoot/security/student-sessions.json`. `StudentApp` drží token jen v paměti.

Upload výsledků používá:

```http
Authorization: Bearer <student-session-token>
```

Při `401` nebo `403` `StudentApp` smaže token z paměti, odhlásí žáka a výsledek ponechá lokálně.

## Student login lockout

Commit: `0ba8cbf Harden student login brute force protection.`

Lockout ochrana je file-based a používá hashované klíče. Stav je uložený v `DataRoot/security/student-login-lockouts.json`.

Vrstvy ochrany:
- student + IP: `classId + loginCode + IP`, 5 neúspěšných pokusů, okno 10 minut, lockout 15 minut,
- student globálně: `classId + loginCode`, 10 neúspěšných pokusů, okno 30 minut, lockout 30 minut,
- class + IP: `classId + IP`, 30 neúspěšných pokusů, okno 10 minut, lockout 30 minut.

API při lockoutu vrací `423 Locked` a hlavičku `Retry-After`. Pokud narazí více vrstev současně, používá se nejdelší zbývající lockout. `StudentApp` zobrazuje českou zprávu:

```text
Příliš mnoho neúspěšných pokusů. Zkus to prosím později.
```

Commit: `bf5b07d Handle invalid student login lockout files.`

`StudentLoginLockoutStore` bezpečně načítá neexistující soubor, prázdný soubor, whitespace-only soubor, `[]`, `{}`, starý slovníkový formát i poškozený JSON. Poškozený lockout soubor nesmí shodit login a po dalším zápisu se uloží nový validní formát.

## Redakce API logů

Commit: `3f96ff2 Redact student identifiers from API logs.`

Serverové auth-related logy nesmí obsahovat konkrétní jména žáků, `studentId`, `loginCode`, PIN, `newPIN`, `PinHash`, `PinSalt`, token, `Bearer` ani jiné citlivé identifikátory. Logování má používat jen obecné příznaky, například `HasConfiguredStudent`, `AccountFound`, `IsActive`, `success`, `requiresCredentialChange` a `requiresStudentConfigurationReload`.

Klientské diagnostické logy ve `StudentApp` a `TeacherApp` redigují konkrétní `studentId`, session id a citlivé texty výsledků na obecné příznaky typu `HasStudentId`, `HasConfiguredStudent` a `HasMessage`.

## TeacherApp a admin správa učitelů

`TeacherApp` před přihlášením nezobrazuje interní panely. Po přihlášení vidí běžný `Teacher` učitelské rozhraní bez sekce `Správa učitelů`. `Admin` vidí navíc `Správa učitelů`.

Role jsou `Admin` a `Teacher`. Server chrání admin endpointy rolí `Admin`; běžný `Teacher` dostává `403`, neplatný nebo nepřihlášený uživatel `401`.

Bezpečnostní pravidla admin správy:
- nelze odstranit, deaktivovat ani demotovat posledního aktivního `Admina`,
- nelze odstranit právě přihlášeného `Admina`,
- odstranění učitele nemaže audit ani žákovská data,
- odstranění učitele zneplatní jeho sessions,
- admin API nikdy nevrací hashe, salty, tokeny ani sessions.

Učitelské heslo musí mít alespoň 12 znaků. Text pravidla v UI je:

```text
Heslo musí mít alespoň 12 znaků.
```

`username` je technický login bez mezer a diakritiky. Povolené znaky jsou `a-z`, číslice, tečka, pomlčka a podtržítko; maximální délka je 64 znaků. `displayName` smí obsahovat českou diakritiku.

## Server

- Aktivní VPS: `89.221.212.49`.
- OS: Ubuntu 24.04.4 LTS.
- Hostname: `schoolmath-server`.
- API služba: `schoolmath-api.service`.
- API běží z `/home/schoolmath/api/SchoolMathTrainer.Api.dll`.
- API poslouchá jen na `127.0.0.1:5078`.
- Nginx je veřejná vstupní brána.
- Dočasný veřejný endpoint: `http://89.221.212.49`.
- Port `5078` se zvenku nepoužívá.
- DataRoot: `/var/lib/schoolmath/data`.
- Security data: `/var/lib/schoolmath/data/security`.

## Monitoring a zálohy

- `ssh-alert-watcher.sh`, cooldown 3600 sekund.
- `send-telegram-alert.sh`.
- Fail2Ban `sshd`: `maxretry 3`, `findtime 10m`, `bantime 1h`.
- `check-security-updates.timer` denně v 07:30.
- `schoolmath-backup.timer` denně, zálohy do `/var/backups/schoolmath`, retence 7.
- `server-stability-check` každých 5 minut.
- `schoolmath-data-integrity-check` každých 15 minut.

## Úklid projektu

Proběhl bezpečný úklid lokálního projektu přes karanténu.

- Karanténa: `C:\Scripts\Codex\archive\SchoolMathTrainer-cleanup-20260425-223913`.
- Audit report: `C:\Scripts\Codex\archive\SchoolMathTrainer-cleanup-20260425-223913\cleanup-report.txt`.
- Přesunuto 979 souborů/složek.
- Karanténu zatím nemazat.
- Po úklidu: build OK, testy 37/37, git status čistý, `TeacherApp` i `StudentApp` funkční.

## Spouštění

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

## Ověření

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
- Nepřenášet `sample-data` ze starého serveru ani ji nevydávat za produkční snapshot.
- Nevystavovat API přímo na `0.0.0.0:5078`.
- Nepřipojovat klienty na externí port `5078`.
- Nevkládat hesla, PINy, tokeny, privátní klíče ani jiná citlivá data do dokumentace nebo repozitáře.
- Nevydávat `TeacherDashboard` za hlavní učitelskou aplikaci.

## Zbývající TODO

- Jednoduchá nástěnka oznámení pro učitele: `Admin` publikuje krátké oznámení a `Teacher` ho po přihlášení vidí; bez chatu, odpovědí, vláken, notifikací a realtime.
- Doména a DNS-based HTTPS.
- Finální release balíček: Windows PowerShell verification procedure, bezpečnostní skeny, SHA-256 hash výstup, Windows code signing `StudentApp.exe`, macOS signing/notarization `TeacherApp.app`, školní schvalovací balíček pro vedení/IT.

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

## Konfigurace klientského endpointu

Onboarding soubor `.smtcfg` obsahuje pole `apiBaseUrl`, které určuje serverový endpoint pro klientské aplikace.

Aktuální dočasný produkční endpoint je:

```text
http://89.221.212.49
```

Port `5078` se z klientských aplikací nepoužívá. API běží na serveru pouze lokálně na `127.0.0.1:5078` za nginxem.
