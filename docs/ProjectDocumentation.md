# SchoolMathTrainer - technická dokumentace

Aktualizováno: 2026-04-20

Dokumentace popisuje aktuální stav pracovního stromu projektu `SchoolMathTrainer` a dnešní ověřený produkční stav po nasazení teacher session token autentizace na aktivní server `89.221.212.49`. Dokumentace je uložená mimo projektovou složku a nesmí být kopírována do `SchoolMathTrainer`.

## Umístění projektu a dokumentace

- Projekt: `C:\Scripts\Codex\apps\src\Aplikace_skola_pocitani\SchoolMathTrainer`
- Dokumentace: `C:\Scripts\Docs\SchoolMathTrainer\ProjectDocumentation.md`
- Dokumentace je samostatný externí artefakt. Staré verze dokumentace se neudržují uvnitř projektu.

## Automatická aktualizace dokumentace

Po každé změně projektu je nutné zkontrolovat, zda se změnila architektura, endpointy, autentizace, onboarding, konfigurace nebo serverové workflow. Pokud ano, přepsat tento soubor tak, aby odpovídal aktuálnímu kódu.

Aktualizační pravidla:

1. Číst aktuální pracovní strom projektu, ne pouze poslední commit.
2. Zkontrolovat zejména `src/SchoolMathTrainer.Api/Program.cs`, služby ve `src/SharedCore/Services`, klientské API třídy v `TeacherApp` a `StudentApp`, `appsettings.json` soubory a případné server/deploy poznámky.
3. Přepsat `C:\Scripts\Docs\SchoolMathTrainer\ProjectDocumentation.md` v UTF-8.
4. Nevytvářet dokumentační kopie nebo zálohy uvnitř `SchoolMathTrainer`.
5. Pokud změna projektu nemění dokumentované chování, lze obsah ponechat, ale stav musí být vědomě zkontrolovaný.


## 0. Produkční stav po deployi 2026-04-20

Backend změny pro teacher session token autentizaci byly nasazeny na aktivní produkční server `89.221.212.49`. Starý kompromitovaný server se nepoužívá.

Aktuální runtime stav ověřený po deployi:

- systemd služba: `schoolmath-api.service`
- runtime assembly: `/home/schoolmath/api/SchoolMathTrainer.Api.dll`
- `WorkingDirectory=/home/schoolmath/api`
- `ExecStart=/usr/bin/dotnet /home/schoolmath/api/SchoolMathTrainer.Api.dll`
- `ASPNETCORE_URLS=http://127.0.0.1:5078`
- interní API naslouchá na `127.0.0.1:5078`
- veřejný vstup jde přes nginx na `http://89.221.212.49`
- služba běží jako uživatel `schoolmath`
- data root API: `/var/lib/schoolmath/data`
- produkční třída: `/var/lib/schoolmath/data/production`
- security část data rootu: `/var/lib/schoolmath/data/security`

Před deployem byla vytvořena serverová záloha:

- záloha: `/home/schoolmath/deploy/backups/api-before-teacher-session-20260420-191529`
- typ zálohy: kopie celé runtime složky `/home/schoolmath/api`
- záloha byla ověřena přítomností `SchoolMathTrainer.Api.dll`

Nasazené backend runtime soubory:

- `SchoolMathTrainer.Api.deps.json`
- `SchoolMathTrainer.Api.dll`
- `SchoolMathTrainer.Api.exe`
- `SchoolMathTrainer.Api.pdb`
- `SchoolMathTrainer.Api.runtimeconfig.json`
- `SchoolMathTrainer.Api.staticwebassets.endpoints.json`
- `SharedCore.dll`
- `SharedCore.pdb`
- `web.config`

Soubor `/home/schoolmath/api/appsettings.json` na serveru nebyl při deployi přepsán.

Produkčně ověřená teacher autentizace:

- teacher endpoint bez tokenu vrací `401 Unauthorized`
- teacher login přes účet `ucitel` vrací token
- teacher endpoint s tokenem vrací `200 OK`
- logout vrací `200 OK`
- stejný token po logoutu vrací `401 Unauthorized`
- teacher endpointy jsou na produkci chráněné `Authorization: Bearer <token>`

Produkčně ověřený student workflow:

- student login funguje bez Bearer tokenu
- student upload výsledků funguje bez Bearer tokenu
- onboarding soubor a formát `.smtcfg` nebyly touto změnou měněny

Ověření TeacherApp:

- přihlášení TeacherApp proti produkčnímu serveru bylo úspěšně ověřeno i přes GUI
- po přihlášení se načetl online seznam žáků
- TeacherApp komunikuje s API přes `http://89.221.212.49`
- informační text v UI aktuálně zobrazuje server s portem `22`; to je SSH/SFTP port a jde o UI nepřesnost, ne problém API komunikace

Při deployi nebyly měněny:

- nginx
- UFW
- fail2ban
- SSH hardening
## 1. Architektura systému

### Hlavní komponenty

- `StudentApp`: WPF desktop aplikace pro žáka. Běží na Windows, používá `SharedCore`, při prvním spuštění importuje `.smtcfg`, přihlašuje žáka přes `loginCode` + PIN a odesílá výsledky na API.
- `TeacherApp`: Avalonia desktop aplikace pro učitele. Umí lokální/file-based režim, online API režim, read-only SFTP načtení serverových dat, správu žáků, reset PINu, mazání žáka, zobrazení přehledů a generování `.smtcfg` souboru pro žáka.
- `SchoolMathTrainer.Api`: ASP.NET Core minimal API na .NET 8. Používá file-based storage přes `IOnlineDataService`, `ConfiguredApiDataService`, `FileClassDataRepository` a sdílené služby z `SharedCore`.
- `SharedCore`: sdílené modely, konfigurace, file storage, výpočty statistik, správa žáků, PIN hashing, onboarding konfigurace, teacher account store a teacher session token service.
- `SchoolMathTrainer.TeacherAdmin`: konzolový admin nástroj pro zakládání a správu teacher účtů ve file-based security složce.
- `nginx`: serverová reverse proxy před API. V projektu není nginx konfigurace uložená jako zdrojový soubor; známý stav je dokumentovaný z kontextu projektu a lokálních konfigurací.

### Projekty v solution

`SchoolMathTrainer.sln` obsahuje:

- `src/SharedCore/SharedCore.csproj` (`net8.0`)
- `src/StudentApp/StudentApp.csproj` (`net8.0-windows`, WPF)
- `src/TeacherApp/TeacherApp.csproj` (`net8.0`, Avalonia 11.3.12)
- `src/SchoolMathTrainer.Api/SchoolMathTrainer.Api.csproj` (`net8.0`, ASP.NET Core Web SDK)
- `src/SchoolMathTrainer.TeacherAdmin/SchoolMathTrainer.TeacherAdmin.csproj` (`net8.0`)

### Tok dat mezi komponentami

1. Učitel pracuje v `TeacherApp`.
2. `TeacherApp` v online režimu zavolá `POST /api/teachers/login`.
3. API ověří učitele přes `TeacherAccountStore` a vydá server-side session token přes `TeacherTokenService`.
4. `TeacherApp` ukládá token jen v paměti instance `TeacherOnlineApiDataSource` a posílá ho v hlavičce `Authorization: Bearer <token>` na teacher endpointy.
5. Teacher endpointy čtou a mění file-based data třídy přes `FileClassDataRepository` a `StudentProgressService`.
6. Učitel vybere žáka a vygeneruje `.smtcfg` soubor přes `StudentConfigFileService`.
7. Žák na prvním spuštění importuje `.smtcfg` ve `StudentApp`.
8. `StudentApp` uloží import do `%LOCALAPPDATA%\SchoolMathTrainer\shared-data-folder.json`.
9. `StudentApp` se přihlašuje na student endpoint přes `loginCode` + PIN. Neposílá teacher token.
10. Po dokončení kola `StudentApp` ukládá výsledek přes student endpoint bez Bearer auth.

### Onboarding workflow

Aktuální onboarding je:

1. `TeacherApp` má načteného žáka v lokálním nebo online režimu.
2. Učitel klikne na generování souboru pro žáka.
3. `TeacherApp.MainWindow.OnGenerateStudentConfigClick` zavolá `StudentConfigFileService.SaveConfigFile`.
4. Vznikne `.smtcfg` JSON soubor. Soubor obsahuje server a identitu žáka, ale neobsahuje PIN.
5. `StudentApp` při prvním spuštění otevře `FirstRunConfigWindow`.
6. `SharedDataFolderSettingsService.ImportFromFile` načte `.smtcfg`.
7. Pokud soubor obsahuje `classId` + `studentId`, uloží se online konfigurace do `%LOCALAPPDATA%\SchoolMathTrainer\shared-data-folder.json`.
8. Žák se přihlásí pomocí `loginCode` + PIN. Při dočasném PINu musí nastavit nový čtyřmístný PIN.

## 2. Backend: SchoolMathTrainer.Api

### Technologie

- .NET 8
- ASP.NET Core Minimal API
- JSON přes `System.Text.Json`
- file-based storage, bez databáze
- sdílené doménové služby v `SharedCore`

### Struktura projektu

- `Program.cs`: registrace služeb a definice endpointů.
- `Services/ConfiguredApiDataService.cs`: mapování `DataConnection:DataRoot` a `ClassId` na souborové cesty.
- `Services/FileClassDataRepository.cs`: API repository nad file-based třídními daty.
- `Services/IClassDataRepository.cs`: kontrakt repository.
- `Services/ApiJson.cs`: sdílené JSON options.
- `appsettings.json`: `DataConnection`, logging, `AllowedHosts`.
- `Properties/launchSettings.json`: lokální běh na `http://localhost:5078`.

### Konfigurace API

`src/SchoolMathTrainer.Api/appsettings.json`:

```json
{
  "DataConnection": {
    "Mode": "LocalFiles",
    "ApiBaseUrl": "http://localhost:5078",
    "ClassId": "production",
    "DataRoot": "/var/lib/schoolmath/data"
  }
}
```

Význam:

- `DataConnection:DataRoot`: kořen file-based dat na serveru.
- `DataConnection:ClassId`: výchozí identifikátor třídy, aktuálně `production`.
- `ApiBaseUrl`: klientská hodnota, pro API samotné není hlavní směrovací mechanika.

### FileClassDataRepository

`FileClassDataRepository` je hlavní backendová vrstva nad třídními daty.

Odpovědnosti:

- přeložit `classId` na datovou složku přes `IOnlineDataService.ResolveClassDataRoot`,
- číst `Config/student-accounts.json`,
- číst souhrny `Data/StudentResults/<studentId>/summary.json`,
- číst session soubory z legacy `Data/Sessions` i z `Data/StudentResults/<studentId>/Sessions`,
- vracet safe profily žáků bez PIN hashů,
- zakládat žáky přes `StudentProgressService.CreateStudentAccount`,
- resetovat PIN přes `StudentProgressService.ResetStudentPin`,
- mazat žáka a výsledky přes `StudentProgressService.DeleteStudentAndResults`,
- přihlašovat žáka přes `StudentProgressService.LoginStudent`,
- ukládat výsledky žáka a regenerovat public class overview.

Důležité metody:

- `GetClassStudents`
- `GetStudent`
- `GetClassOverview`
- `GetStudentResultDetail`
- `GetClassActivities`
- `CreateStudent`
- `ResetStudentPin`
- `DeleteStudent`
- `LoginStudent`
- `SaveStudentResult`

### StudentProgressService

`StudentProgressService` je sdílená doménová služba pro práci s žáky a výsledky.

Odpovědnosti:

- správa aktuálně přihlášeného žáka v klientské aplikaci,
- login přes `loginCode` + PIN,
- vynucení změny dočasného PINu,
- ukládání odpovědí a session souborů,
- výpočet souhrnů přes `StatisticsService`,
- zakládání účtů žáků,
- generování `loginCode`, `studentId` a dočasných PINů,
- PBKDF2 hashování PINu (`100000` iterací, SHA-256),
- šifrování dočasného PINu přes `PendingTemporaryPinProtector`,
- mazání žáka včetně výsledků,
- regenerace `Data/Public/class-overview.json`.

### Teacher auth

Teacher autentizace je server-side session systém bez JWT a bez databáze. Token je opaque session token; klient z něj nečte žádná oprávnění ani payload.

Hlavní třídy:

- `TeacherAccountStore`: spravuje `teachers.json` a `teacher-auth-settings.json` v `<DataRoot>\security`.
- `TeacherPasswordHasher`: hashování hesel učitele.
- `TeacherLoginRateLimiter`: paměťové omezení pokusů o login.
- `TeacherTokenService`: vydávání, ověřování a rušení server-side tokenů.
- `TeacherSessionRecord`: položka uložené session.

Soubory v runtime datech:

- `<DataRoot>\security\teachers.json`: teacher účty.
- `<DataRoot>\security\teacher-auth-settings.json`: nastavení auth, zejména `tokenLifetimeMinutes`.
- `<DataRoot>\security\teacher-sessions.json`: server-side session evidence, ukládá hash tokenu, ne plaintext token.

Token flow:

1. `POST /api/teachers/login` přijme `TeacherLoginRequest`.
2. `TeacherAccountStore.VerifyCredentials` ověří username/password a aktivitu účtu.
3. `TeacherTokenService.IssueToken` vytvoří náhodný opaque token z 32 random bytů.
4. Server uloží SHA-256 hash tokenu, ne klientský plaintext token, do `<DataRoot>\\security\\teacher-sessions.json` spolu s `username`, `displayName`, `issuedUtc`, `expiresUtc`, `lastSeenUtc`. Na produkci je reálný session soubor `/var/lib/schoolmath/data/security/teacher-sessions.json`.
5. API vrátí `TeacherLoginResponse` s `token` a `expiresUtc`.
6. `TeacherApp` přidá `Authorization: Bearer <token>` na další teacher requesty.
7. `TryAuthorizeTeacher` v `Program.cs` přečte Bearer token a zavolá `TeacherTokenService.ValidateToken`.
8. Expired sessions se při validaci průběžně odstraňují.
9. `POST /api/teachers/logout` zavolá `TeacherTokenService.RevokeToken`.

### Endpointy

#### Public health endpoint

| Metoda | Cesta | Auth | Popis |
|---|---|---|---|
| GET | `/health` | ne | Vrací stav API. |

#### Teacher auth endpointy

| Metoda | Cesta | Auth | Popis |
|---|---|---|---|
| POST | `/api/teachers/login` | ne | Ověří učitele jménem a heslem, vrátí session token. |
| POST | `/api/teachers/logout` | `Authorization: Bearer` | Zruší aktuální teacher token. |

#### Teacher endpointy chráněné Bearer tokenem

| Metoda | Cesta | Auth | Popis |
|---|---|---|---|
| GET | `/api/classes/{classId}` | `Authorization: Bearer` | Seznam žáků a safe profily. |
| GET | `/api/classes/{classId}/overview` | `Authorization: Bearer` | Třídní přehled. |
| GET | `/api/classes/{classId}/activities?limit=10` | `Authorization: Bearer` | Poslední aktivity třídy, limit 1 až 50. |
| GET | `/api/students/{classId}/{studentId}` | `Authorization: Bearer` | Detail žáka. |
| GET | `/api/students/{classId}/{studentId}/results` | `Authorization: Bearer` | Detail výsledků žáka. |
| POST | `/api/classes/{classId}/students` | `Authorization: Bearer` | Vytvoří žáka, vrací temporary PIN. |
| POST | `/api/students/{classId}/{studentId}/reset-pin` | `Authorization: Bearer` | Resetuje PIN žáka a vrací temporary PIN. |
| DELETE | `/api/students/{classId}/{studentId}` | `Authorization: Bearer` | Smaže žáka a pokusí se smazat výsledky. |

Neplatný nebo chybějící token vrací `401 Unauthorized`.

#### Student endpointy bez teacher tokenu

| Metoda | Cesta | Auth | Popis |
|---|---|---|---|
| POST | `/api/classes/{classId}/login` | ne | Přihlášení žáka přes `loginCode`, PIN, volitelně `newPin`, a `studentId` z `.smtcfg`. |
| POST | `/api/students/{classId}/{studentId}/results` | ne | Uloží session výsledek žáka. |

Student endpointy záměrně nepoužívají teacher Bearer token, aby onboarding a StudentApp workflow zůstaly beze změny.

## 3. TeacherApp

### Technologie a struktura

- .NET 8
- Avalonia 11.3.12
- `src/TeacherApp/MainWindow.axaml` a `MainWindow.axaml.cs`: hlavní UI a orchestrace.
- `src/TeacherApp/Data`: datové zdroje, čtečky výsledků, modely výsledků pro UI.
- `src/TeacherApp/Settings`: server/SFTP nastavení.
- `src/TeacherApp/appsettings.json`: výchozí režim a API nastavení.

### Režimy práce

`TeacherApp` používá `AppConfiguration.DataConnection.Mode`:

- `LocalFiles`: čte a zapisuje lokální/shared file-based data.
- `OnlineApi`: používá `TeacherOnlineApiDataSource` a teacher endpointy API.

Aplikace má navíc read-only SFTP načtení serverových dat přes `TeacherSftpReadOnlyDataSource`.

### Přihlášení učitele

1. V online režimu se po startu zobrazí login panel.
2. `MainWindow.OnTeacherLoginClick` zavolá `TeacherOnlineApiDataSource.LoginTeacher`.
3. Klient pošle `POST api/teachers/login` s `TeacherLoginRequest`.
4. Po úspěchu uloží token do privátního pole `_teacherToken`.
5. Nastaví `HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _teacherToken)`.
6. UI se přepne do přihlášeného stavu a načte data třídy.

Token se neukládá na disk; žije pouze v paměti běžící instance `TeacherOnlineApiDataSource`.

### Logout učitele

- `MainWindow.OnTeacherLogoutClick` volá `TeacherOnlineApiDataSource.LogoutTeacher`.
- Klient pošle `POST api/teachers/logout` s Bearer tokenem.
- Lokálně se token smaže voláním `ClearAuthentication`.
- UI se vrátí do odhlášeného stavu.

### Práce s API

`TeacherOnlineApiDataSource` volá:

- `POST api/teachers/login`
- `POST api/teachers/logout`
- `GET api/classes/{classId}`
- `GET api/classes/{classId}/activities?limit=...`
- `GET api/students/{classId}/{studentId}/results`
- `POST api/classes/{classId}/students`
- `POST api/students/{classId}/{studentId}/reset-pin`
- `DELETE api/students/{classId}/{studentId}`

Při `401` nebo `403` nastaví `LastAuthorizationFailed` a UI vynutí nové přihlášení.

### Generování onboarding souboru

`MainWindow.OnGenerateStudentConfigClick`:

1. ověří vybraného žáka,
2. v online režimu ověří teacher auth,
3. určí `classId`,
4. nabídne uložení `<LoginCode>.smtcfg`,
5. zavolá `StudentConfigFileService.SaveConfigFile(path, classId, studentId, apiBaseUrl)`.

Soubor neobsahuje PIN. PIN se žákovi předává samostatně.

## 4. StudentApp

### Technologie a struktura

- .NET 8 Windows
- WPF (`UseWPF=true`)
- `App.xaml.cs`: start aplikace, první konfigurace, DI-like ruční sestavení služeb.
- `Views`: login, shell, režimy procvičování, výsledky.
- `ViewModels`: stav UI a příkazy.
- `Services/StudentOnlineLoginService.cs`: online login žáka.
- `Services/StudentOnlineResultService.cs`: upload výsledků.

### První spuštění

1. `App.OnStartup` načte `SharedDataFolderSettingsService.Load()`.
2. Pokud chybí validní importovaná student konfigurace, otevře `FirstRunConfigWindow`.
3. Uživatel vybere `.smtcfg` soubor od učitele.
4. `SharedDataFolderSettingsService.ImportFromFile` soubor ověří.
5. Nastavení se uloží do `%LOCALAPPDATA%\SchoolMathTrainer\shared-data-folder.json`.
6. Aplikace pokračuje do `StudentShellWindow`.

### Připojení k API

`StudentApp` určuje API base URL takto:

1. preferuje `ApiBaseUrl` z importovaného `.smtcfg` uloženého ve `shared-data-folder.json`,
2. fallback je `configuration.DataConnection.ApiBaseUrl` z `src/StudentApp/appsettings.json`,
3. fallback výchozí konstanta je `http://89.221.212.49`.

### Login žáka

`StudentLoginViewModel.LoginStudent` používá:

- `StudentOnlineLoginService.LoginAsync`, pokud je online konfigurace dostupná,
- jinak lokální `StudentProgressService.LoginStudent`.

Online request:

```http
POST /api/classes/{classId}/login
```

Body obsahuje:

- `loginCode`
- `pin`
- `newPin`
- `studentId` z `.smtcfg`

API kontroluje, že `studentId` odpovídá nakonfigurovanému účtu a `loginCode`. Při dočasném PINu vrací požadavek na změnu PINu.

### Ukládání výsledků

Po dokončení kola `StudentOnlineResultService.SaveCompletedRoundAsync` posílá:

```http
POST /api/students/{classId}/{studentId}/results
```

Body je `StudentSession`. Endpoint není chráněn teacher tokenem.

### Lokální konfigurace v `%LOCALAPPDATA%`

Používané soubory:

- `%LOCALAPPDATA%\SchoolMathTrainer\shared-data-folder.json`: import `.smtcfg`, classId, studentId, apiBaseUrl, dataFolderPath.
- `%LOCALAPPDATA%\SchoolMathTrainer\teacher-server-settings.json`: TeacherApp server/SFTP nastavení.
- `%LOCALAPPDATA%\SchoolMathTrainer\teacher-sftp-cache\...`: cache read-only SFTP dat pro TeacherApp.

## 5. Konfigurační soubory

### `.smtcfg`

Generuje `StudentConfigFileService`. Aktuální formát:

```json
{
  "version": 1,
  "classId": "production",
  "studentId": "STUDENTID",
  "apiBaseUrl": "http://89.221.212.49"
}
```

Význam:

- `version`: verze formátu, aktuálně `1`.
- `classId`: identifikátor třídy na API.
- `studentId`: pevná identita žáka, pro kterého je soubor určen.
- `apiBaseUrl`: adresa API/reverse proxy.

Legacy import umí také `dataFolderPath` a `classFolderName`, protože `SharedDataFolderSettings` má širší model.

### `shared-data-folder.json`

Cesta:

```text
%LOCALAPPDATA%\SchoolMathTrainer\shared-data-folder.json
```

Model `SharedDataFolderSettings`:

- `version`
- `classId`
- `classFolderName`
- `dataFolderPath`
- `studentId`
- `apiBaseUrl`
- `isStudentConfigurationImported`

Online import vytvoří lokální data folder přes `OnlineDataService.ResolveClassDataRoot(classId)` a nastaví `IsStudentConfigurationImported=true`.

### API konfigurace

`SchoolMathTrainer.Api/appsettings.json`:

- `DataConnection:DataRoot`: produkční data root, aktuálně `/var/lib/schoolmath/data`.
- `DataConnection:ClassId`: výchozí třída, aktuálně `production`.
- `DataConnection:ApiBaseUrl`: lokální hodnota `http://localhost:5078`.

`TeacherApp/appsettings.json` a `StudentApp/appsettings.json`:

- `SharedDataRoot`: relativní cesta na `sample-data` pro lokální režim.
- `DataConnection:Mode`: aktuálně `1`, tedy online režim podle enumu `ApplicationDataMode`.
- `DataConnection:ApiBaseUrl`: `http://89.221.212.49`.
- `DataConnection:ClassId`: `production`.

## 6. Server

Aktivní produkční server je pouze `89.221.212.49`. Starý kompromitovaný server se nepoužívá.

### Aktuální runtime

Produkční stav ověřený po deployi teacher session token autentizace:

- služba: `schoolmath-api.service`
- uživatel služby: `schoolmath`
- runtime složka: `/home/schoolmath/api`
- runtime assembly: `/home/schoolmath/api/SchoolMathTrainer.Api.dll`
- `WorkingDirectory=/home/schoolmath/api`
- `ExecStart=/usr/bin/dotnet /home/schoolmath/api/SchoolMathTrainer.Api.dll`
- `ASPNETCORE_URLS=http://127.0.0.1:5078`
- interní API: `http://127.0.0.1:5078`
- veřejný vstup přes nginx: `http://89.221.212.49`
- API data root: `/var/lib/schoolmath/data`
- produkční class data: `/var/lib/schoolmath/data/production`
- security data: `/var/lib/schoolmath/data/security`

### systemd služba

`schoolmath-api.service` je načtená z `/etc/systemd/system/schoolmath-api.service` a má drop-in override `/etc/systemd/system/schoolmath-api.service.d/override.conf`. Override nastavuje finální bind adresu na `127.0.0.1:5078`, takže Kestrel není přímo vystaven veřejně.

Služba byla po deployi restartována přes systemd autorestart ukončením vlastního procesu `dotnet`, protože přímé `systemctl restart` vyžadovalo interaktivní autentizaci a passwordless sudo nebylo povoleno. Po restartu služba běžela jako `active (running)` a healthcheck na localhost vracel `200 OK`.

### nginx reverse proxy

Veřejný vstup je `http://89.221.212.49`. nginx nebyl při deployi teacher session token autentizace měněn. Reverse proxy směřuje veřejný HTTP vstup na interní API poslouchající na `127.0.0.1:5078`.

### Porty a firewall (UFW)

UFW nebylo při deployi měněno. Dokumentovaný a ověřený stav aplikace předpokládá veřejný HTTP vstup přes nginx a interní Kestrel bind pouze na localhostu.

### fail2ban

fail2ban nebyl při deployi měněn. Aplikační ochrana teacher loginu zůstává v `TeacherLoginRateLimiter`; serverové rate limiting a audit logy jsou možné budoucí kroky, ne součást tohoto deploye.

### Serverová záloha a deploy

Před deployem vznikla záloha `/home/schoolmath/deploy/backups/api-before-teacher-session-20260420-191529`. Šlo o kopii celé runtime složky `/home/schoolmath/api` mimo aktivní runtime složku.

Nasazené backend runtime soubory:

- `SchoolMathTrainer.Api.deps.json`
- `SchoolMathTrainer.Api.dll`
- `SchoolMathTrainer.Api.exe`
- `SchoolMathTrainer.Api.pdb`
- `SchoolMathTrainer.Api.runtimeconfig.json`
- `SchoolMathTrainer.Api.staticwebassets.endpoints.json`
- `SharedCore.dll`
- `SharedCore.pdb`
- `web.config`

Serverový `appsettings.json` nebyl přepsán.
## 7. Bezpečnost

### SSH hardening

Doporučený cílový stav:

- key-only SSH přístup,
- vypnuté password login pro SSH,
- nepoužívat root login,
- omezený uživatel `schoolmath`,
- oddělit deploy oprávnění od běhu API,
- auditovat práva na data root a security soubory.

### Oddělení veřejného a interního portu

- Veřejná cesta pro klienty má být nginx na `80` nebo `443`.
- ASP.NET Core API má běžet interně na `127.0.0.1:5078`.
- StudentApp a TeacherApp nemají volat interní port přímo z internetu.

### Teacher API ochrana

Chráněné Bearer tokenem:

- teacher data reads,
- teacher přehledy,
- vytváření žáka,
- reset PINu,
- mazání žáka,
- logout.

Nechráněné teacher tokenem:

- `POST /api/teachers/login`, protože teprve vydává token,
- student login endpoint,
- student result upload endpoint,
- health endpoint.

### Co je chráněné a co ne

Chráněné:

- teacher credentials jsou hashovaná v `teachers.json`,
- teacher token je opaque a server ukládá hash tokenu,
- teacher endpointy vyžadují Bearer token,
- PIN žáka je PBKDF2 hash se saltem,
- temporary PIN je chráněný přes `PendingTemporaryPinProtector`.

Nechráněné nebo slabší:\n\n- Bezprostřední doporučený krok po deployi 2026-04-20 je změnit dočasné teacher heslo účtu `ucitel` na finální bezpečné heslo mimo chat.

- student endpointy nevyžadují teacher auth,
- student result upload spoléhá na znalost `classId` a `studentId`,
- bez HTTPS by Bearer token i PINy šly po síti jako HTTP provoz,
- teacher session evidence je lokální JSON soubor, ne distribuovaný session store,
- `TeacherLoginRateLimiter` je in-memory a resetuje se restartem API,
- dokumentovaná serverová konfigurace není v repozitáři jako kód.

## 8. Build a spuštění

### Lokální build

```powershell
dotnet build .\SchoolMathTrainer.sln
```

### Spuštění API lokálně

```powershell
dotnet run --project .\src\SchoolMathTrainer.Api\SchoolMathTrainer.Api.csproj --urls http://localhost:5078
```

### Spuštění TeacherApp

```powershell
dotnet run --project .\src\TeacherApp\TeacherApp.csproj
```

### Spuštění StudentApp

```powershell
dotnet run --project .\src\StudentApp\StudentApp.csproj
```

### Teacher admin nástroj

Příklady:

```powershell
dotnet run --project .\src\SchoolMathTrainer.TeacherAdmin\SchoolMathTrainer.TeacherAdmin.csproj -- list-teachers --data-root C:\path\to\data

dotnet run --project .\src\SchoolMathTrainer.TeacherAdmin\SchoolMathTrainer.TeacherAdmin.csproj -- create-teacher --username ucitel --display-name "Učitel" --password "heslo" --data-root C:\path\to\data
```

Na serveru je default data root `/var/lib/schoolmath/data`.

### Deployment na server

Aktuální repozitář obsahuje `publish` výstupy, ale přesný deploy skript není součástí zdrojové dokumentace.

Doporučený postup:

1. `dotnet publish .\src\SchoolMathTrainer.Api\SchoolMathTrainer.Api.csproj -c Release -o .\publish\api`
2. Zkopírovat publish výstup na VPS do aplikační složky.
3. Zkontrolovat `appsettings.json` nebo environment proměnnou `DataConnection__DataRoot`.
4. Restartovat `schoolmath-api.service`.
5. Ověřit `GET /health` přes nginx.
6. Ověřit teacher login, chráněný teacher endpoint a student login/result upload.

## 9. Omezení a známé slabiny

- Není implementována databáze; všechna data jsou file-based JSON.
- Teacher session storage je JSON soubor; pro více instancí API by bylo potřeba sdílené session úložiště nebo sticky sessions.
- Token lifetime je v `teacher-auth-settings.json`, ale není zde role/permission model.
- Student endpointy nejsou chráněné teacher tokenem; to je záměr kvůli StudentApp workflow, ale je to bezpečnostní riziko pro veřejný internet.
- Bez HTTPS je celé řešení citlivé na odposlech. Klientské konfigurace nyní používají `http://89.221.212.49`.
- Serverová konfigurace nginx, systemd, UFW a fail2ban není verzovaná v repozitáři.
- V projektu existuje historická dokumentace/README zmiňující `TeacherDashboard`, zatímco aktuální solution obsahuje `TeacherApp`.
- Některé konzolové výpisy v `SchoolMathTrainer.TeacherAdmin` a část textu v `TeacherTokenService` mohou v PowerShell výpisu působit jako poškozená diakritika; soubory je potřeba držet jako UTF-8 a při dalších zásazích ověřit skutečné kódování.
- `TeacherApp` ukládá token jen v paměti, takže po restartu aplikace je nutné nové přihlášení.
- `TeacherLoginRateLimiter` je in-memory; restart API smaže historii pokusů.
- `TeacherServerSettings.DefaultRemoteDataPath` (`/srv/schoolmath/data`) se liší od API `DataRoot` (`/var/lib/schoolmath/data`).\n- UI TeacherApp aktuálně může v informačním textu zobrazovat server s portem `22`; jde o SSH/SFTP port a UI nepřesnost, ne problém API komunikace.\n- Další možné kroky: oprava UI textu se serverem a portem `22`, jednoduché admin rozhraní pro správu učitelů, případné rate limiting a audit logy později.

## 10. Kontrolní checklist při změnách projektu

Při každé změně zkontrolovat:

- Přibyly, zmizely nebo se změnily endpointy v `Program.cs`?
- Změnilo se, které endpointy vyžadují `Authorization: Bearer`?
- Změnil se formát `.smtcfg` nebo `shared-data-folder.json`?
- Změnil se StudentApp první start, login nebo upload výsledků?
- Změnil se TeacherApp login, logout, ukládání tokenu nebo onboarding?
- Změnil se `DataRoot`, `ClassId`, `ApiBaseUrl` nebo runtime struktura dat?
- Změnil se server deploy, nginx, firewall nebo systemd postup?
- Změnily se bezpečnostní předpoklady nebo slabiny?

Pokud ano, přepsat tento dokument.