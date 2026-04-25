# SchoolMathTrainer - projektová dokumentace

Aktualizováno: 2026-04-25

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

## TeacherApp UI

`TeacherApp` má bezpečně oddělený nepřihlášený a přihlášený stav. Před přihlášením učitele je viditelná pouze přihlašovací část učitelského účtu a bezpečný stav aplikace. Učitelská data se bez přihlášení nenačítají.

Před přihlášením nejsou vidět:
- hlavní akce pro načítání a obnovu dat,
- seznam žáků,
- vytvoření nového žáka,
- třídní přehled,
- detail žáka,
- akce se žákem,
- výsledky žáka,
- poslední hry žáka,
- poslední aktivity třídy.

Po úspěšné autentizaci učitele se interní učitelská administrace zobrazí a funguje stejně jako dříve: hlavní akce, seznam žáků, vytvoření žáka, třídní přehled, detail žáka, reset PINu, generování `.smtcfg`, výsledky žáka a aktivity třídy. Po odhlášení nebo ztrátě teacher session se interní panely opět skryjí a zobrazená data se vyčistí podle existující logiky aplikace.

Po přihlášení s rolí `Admin` je navíc viditelná sekce `Správa učitelů`. Běžný `Teacher` tuto sekci nevidí. Admin může v UI načíst seznam učitelů, přidat učitele, upravit zobrazované jméno, změnit roli, resetovat heslo, aktivovat, deaktivovat a odstranit učitele. Admin UI používá server-side chráněné `/api/admin/*` endpointy; hesla se neposílají v příkazech, nelogují se a neukládají se do dokumentace. Odstranění učitele nemaže audit ani žákovská data a server nedovolí odstranit posledního `Admin`, posledního aktivního `Admin` ani právě přihlášeného administrátora.

## StudentApp UI

`StudentApp` má aktuálně modernizované dětské WPF rozhraní. Vizuálně používá sytější, ale stále přátelskou pastelovou paletu, kompaktní horní hlavičku `Školní počítání` a přehledné karty bez starých velkých gradientových bloků.

Nepřihlášená obrazovka:
- zobrazuje načtený žákovský soubor nebo dostupnou identifikaci žáka z `.smtcfg`,
- upozorňuje, že přihlašovací kód musí odpovídat načtenému souboru,
- má dostupné tlačítko `Změnit žáka` ještě před přihlášením,
- drží sjednocené levé zarovnání hlavičky, nadpisu, informačního boxu, popisků polí a hlavní akce `Vstoupit`.

Přihlášená obrazovka:
- má kompaktní stavový panel přihlášeného žáka,
- nabízí sekci `Vyber si procvičování`,
- nabízí sekci `Výsledky`,
- zobrazuje aktivní obsah hry nebo výsledků ve spodní části,
- zachovává herní obrazovku bez svislého scrollu na běžném desktopovém rozlišení.

Dostupné režimy a akce:

| Prvek | Popis |
|---|---|
| `Začátečník` | Počítání do 20. |
| `Pokročilý` | Počítání do 20. |
| `Nová hra` | Spuštění nového kola aktuálního procvičování. |
| `Můj výsledek` | Zobrazení výsledků přihlášeného žáka. |
| `Třídní výsledky` | Zobrazení třídních výsledků. |

Modernizace UI nemění backend, API endpointy, onboarding formát, login flow, session token flow ani upload výsledků.

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
- podporuje role `Admin` a `Teacher`,
- účty bez uložené role se načítají jako `Teacher`,
- admin endpointy pod `/api/admin/*` vyžadují roli `Admin`,
- poslední aktivní `Admin` nesmí být deaktivován ani převeden na `Teacher`,
- učitelský účet lze odstranit jen server-side chráněným admin endpointem,
- nelze odstranit posledního `Admin` ani právě přihlášeného administrátora,
- `TeacherApp` zobrazuje sekci `Správa učitelů` jen přihlášenému `Admin`; běžný `Teacher` ji nevidí,
- `TeacherApp` před úspěšným přihlášením nezobrazuje interní administraci a nenačítá data třídy,
- po odhlášení nebo ztrátě session se interní panely skryjí a zobrazená data se vyčistí,
- neukládá token do `.smtcfg`,
- neukládá token na disk klienta,
- server ukládá pouze hash tokenu do security evidence.

První admin účet se po deployi vytváří přes `TeacherAdmin` CLI. Heslo se zadává interaktivně a nesmí být předané v příkazu:

```powershell
dotnet /home/schoolmath/api/SchoolMathTrainer.TeacherAdmin.dll create-teacher --username admin --display-name Admin --role Admin --data-root /var/lib/schoolmath/data
```

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

Admin endpointy s Bearer tokenem a rolí `Admin`:

| Metoda | Endpoint | Popis |
|---|---|---|
| `GET` | `/api/admin/teachers` | Bezpečný seznam učitelů bez hashů, saltů, tokenů a sessions. |
| `POST` | `/api/admin/teachers` | Vytvoření učitelského účtu. |
| `PUT` | `/api/admin/teachers/{username}` | Změna zobrazovaného jména nebo role. |
| `POST` | `/api/admin/teachers/{username}/reset-password` | Reset hesla učitele. |
| `POST` | `/api/admin/teachers/{username}/deactivate` | Deaktivace učitele. |
| `POST` | `/api/admin/teachers/{username}/activate` | Aktivace učitele. |
| `DELETE` | `/api/admin/teachers/{username}` | Odstranění učitele se zneplatněním jeho sessions. |

Admin endpointy auditují vytvoření, úpravu, reset hesla, aktivaci, deaktivaci, odstranění a změnu role učitele. Audit neobsahuje hesla, hashe, salty, tokeny ani PINy.

Odstranění učitele nemaže audit, žákovská data ani `teacher-auth-settings.json`. Pro běžné vypnutí účtu je vhodnější deaktivace.

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
- student session token se drží jen v paměti `StudentApp` a neukládá se do `.smtcfg` ani na disk klienta,
- výsledky se uploadují jen s platným student Bearer tokenem,
- při `401` nebo `403` se žák odhlásí a neodeslaný výsledek zůstane lokálně,
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

- Nepoužívat starý server `89.221.220.226`; je kompromitovaný a nesmí se používat.
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
