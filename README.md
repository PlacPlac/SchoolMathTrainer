# SchoolMathTrainer

SchoolMathTrainer je desktopové řešení pro školní procvičování matematiky.

Aktuální řešení má tyto hlavní části:
- `TeacherApp`: učitelská desktop aplikace v Avalonia UI.
- `StudentApp`: žákovská desktop aplikace ve WPF.
- `SchoolMathTrainer.Api`: ASP.NET Core backend.
- `SharedCore`: sdílené modely a služby.
- `SchoolMathTrainer.TeacherAdmin`: konzolový nástroj pro správu učitelských účtů.

## StudentApp

`StudentApp` má modernizované dětské rozhraní s barevnější pastelovou paletou, kompaktní horní hlavičkou a přehlednými kartami. Před přihlášením zobrazuje, pro kterého žáka nebo žákovský soubor je načtená konfigurace, a tlačítko `Změnit žáka` je dostupné už na nepřihlášené obrazovce. Přihlašovací karta drží přehledné levé zarovnání textů, polí a hlavní akce.

Po přihlášení je hlavní obrazovka rozdělená na sekci `Vyber si procvičování`, sekci `Výsledky` a aktivní obsah hry nebo výsledků. Herní obsah je navržený tak, aby na běžné obrazovce fungoval bez svislého scrollu.

Dostupné režimy a akce:
- `Začátečník` - Počítání do 20.
- `Pokročilý` - Počítání do 20.
- `Nová hra`.
- `Můj výsledek`.
- `Třídní výsledky`.

Zachované bezpečnostní chování:
- žák se přihlašuje přes `loginCode` + PIN,
- po úspěšném přihlášení vzniká student session token,
- student session token se drží jen v paměti klienta,
- `.smtcfg` neobsahuje PIN ani token,
- upload výsledků vyžaduje platný student Bearer token,
- při `401` nebo `403` se žák odhlásí a výsledek zůstane lokálně.

## Aktuální produkční stav

- Hlavní učitelská aplikace je `TeacherApp`. `TeacherDashboard` už není aktuální název ani hlavní GUI.
- Produkční server je `89.221.212.49`.
- Produkční klientský `apiBaseUrl` je aktuálně pouze `http://89.221.212.49`.
- Žákovský onboarding probíhá přes soubor `.smtcfg`, který generuje `TeacherApp`.
- Veřejný provoz jde přes nginx. Aplikace se nemají připojovat přímo na veřejný port Kestrelu.
- Učitelské endpointy vyžadují Bearer token. Žákovský login je samostatný flow a po úspěchu vydává student session token pro upload výsledků.

## Lokální vývoj

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

Spuštění API lokálně:

```powershell
dotnet run --project .\src\SchoolMathTrainer.Api\SchoolMathTrainer.Api.csproj
```

Správa teacher účtů:

```powershell
dotnet run --project .\src\SchoolMathTrainer.TeacherAdmin\SchoolMathTrainer.TeacherAdmin.csproj -- list-teachers --data-root C:\path\to\data
```

Praktické lokální soubory:
- `%LOCALAPPDATA%\SchoolMathTrainer\api-data` je výchozí lokální data root API, pokud není přepsaný konfigurací.
- `%LOCALAPPDATA%\SchoolMathTrainer\teacher-server-settings.json` ukládá nastavení serveru pro učitelskou aplikaci.
- `%LOCALAPPDATA%\SchoolMathTrainer\shared-data-folder.json` ukládá importovaný onboarding nebo sdílenou datovou složku žáka.

## Server a bezpečnost

- Produkční host je `89.221.212.49`.
- Veřejný klientský vstup je `http://89.221.212.49` přes nginx na portu `80`.
- Interní API runtime běží pouze na `127.0.0.1:5078`.
- Produkční data root API je `/var/lib/schoolmath/data`.
- Security data jsou v `/var/lib/schoolmath/data/security`.
- `TeacherApp` má samostatné nastavení SSH/SFTP serveru. Výchozí host je `89.221.212.49`, port `22`, uživatel `schoolmath` a vzdálená cesta `/srv/schoolmath/data`.
- Teacher token se v klientovi drží jen v paměti. Server ukládá jen hash session tokenu.
- Teacher login i student login mají lockout ochranu. Na serveru se počítá s `fail2ban`.
- Do dokumentace ani repozitáře nepatří hesla, privátní klíče, tokeny ani exporty s citlivými daty.

## Onboarding žáka

Postup:
1. Učitel se přihlásí v `TeacherApp`.
2. Učitel vybere žáka a vygeneruje onboarding soubor.
3. `TeacherApp` uloží `<LoginCode>.smtcfg`.
4. `StudentApp` při prvním spuštění vyžádá `.smtcfg`.
5. `StudentApp` import uloží do `%LOCALAPPDATA%\SchoolMathTrainer\shared-data-folder.json`.
6. Žák se následně přihlásí přes `loginCode` + PIN.

Formát `.smtcfg`:

```json
{
  "version": 1,
  "classId": "production",
  "studentId": "STUDENT_ID",
  "apiBaseUrl": "http://89.221.212.49"
}
```

Soubor `.smtcfg`:
- neobsahuje PIN,
- neobsahuje teacher token,
- neobsahuje SSH údaje,
- nesmí obsahovat jiný host, jiný protokol ani vlastní port.

Pokud `studentId` z onboarding souboru neodpovídá zadanému `loginCode`, backend vrátí požadavek na nové načtení konfigurace žáka.

## Autentizace učitele

Primární teacher login endpoint:

```http
POST /api/teacher-auth/login
```

Kompatibilní alias:

```http
POST /api/teachers/login
```

Odhlášení:

```http
POST /api/teachers/logout
```

Teacher autentizace:
- používá username + password,
- vrací opaque session token,
- vyžaduje `Authorization: Bearer <token>` pro teacher endpointy,
- ukládá na serveru jen hash tokenu,
- neukládá token do `.smtcfg` ani na disk klienta.

Žákovská autentizace je oddělená:
- `POST /api/classes/{classId}/login` používá `loginCode`, PIN, volitelný `newPin` a `studentId` z `.smtcfg`,
- úspěšný login vrací `studentSessionToken`,
- upload výsledků na `POST /api/students/{classId}/{studentId}/results` vyžaduje student Bearer token.

## Zálohy a monitoring

- Lokální aplikační logy jsou ve složce `Logs/` a nejsou určené ke commitu.
- Teacher auth zapisuje audit a lockout data do security složky serveru.
- `TeacherApp` může vytvářet lokální SFTP cache v `%LOCALAPPDATA%\SchoolMathTrainer\teacher-sftp-cache`.
- V repozitáři zatím není samostatný provozní runbook pro pravidelné automatické zálohy.
- Monitoring v dokumentaci udržuj jen v rozsahu, který je doložený repem nebo nasazenou konfigurací. Nepopisuj neověřené alarmy nebo automatizace jako hotovou věc.

## Zakázané/nepoužívat

- Nepoužívat starý server `89.221.220.226`; je kompromitovaný a nesmí se používat.
- Nepřenášet `sample-data` ze starého serveru ani ji nevydávat za produkční snapshot.
- Nevystavovat API přímo na `0.0.0.0:5078` ani veřejně nepřipojovat klienty na port `5078`.
- Nevkládat tajné údaje do dokumentace ani do repozitáře.
- Nevydávat HTTPS za povinný klientský endpoint, dokud se nezmění aktuální povolený klientský `apiBaseUrl` v kódu a nasazení.
- Nevydávat `TeacherDashboard` za hlavní učitelskou aplikaci.

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
