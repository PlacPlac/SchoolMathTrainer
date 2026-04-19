# AGENTS.md

## Rozsah

Tyto instrukce plati pro cely repozitar od teto slozky dolu.

## Hlavni cil

Vytvorit a udrzovat kompletni Windows desktop reseni v C# WPF pro skolni procvicovani scitani a odecitani do 20 pro male deti.

Reseni musi byt plne funkcni, buildovatelne ve Visual Studiu a pripravene k pouziti.

## Hranice workspace

Vytvarej a upravuj soubory pouze uvnitr:

`C:\Scripts\Codex\apps\src\Aplikace_skola_pocitani\SchoolMathTrainer`

Nevytvarej ani neupravuj nic mimo tento workspace.

Na konci kazde vetsi prace vypis:
- ktere soubory byly vytvoreny
- ktere soubory byly upraveny
- potvrzeni, ze nic mimo workspace nebylo zmeneno

## Neprekrocitelna pravidla

Nikdy:
- nepouzivej zadny server
- nepouzivej zadnou databazi
- nepouzivej zadny cloud backend
- nenechavej placeholdery
- nenechavej TODO misto implementace
- nenechavej pseudokod
- nevynechavej povinne soubory kvuli delce odpovedi
- nezpristupnuj ucitelsky nebo admin rezim ve StudentApp
- nevytvarej vsechny soubory jen jako prazdne kostry bez realne logiky

Vzdy:
- dodrzuj presne pozadovanou strukturu
- vracej plny obsah souboru, pokud je o nej pozadano
- pokud se vse nevejde do jedne odpovedi, pokracuj v navazujicich castech
- zachovej buildovatelnost projektu
- preferuj standardni .NET a WPF funkce pred zbytecnymi zavislostmi
- u velkych ukolu postupuj po mensich krocich
- nejdriv vytvor buildovatelny zaklad, potom dopln plnou funkcnost

## Doporuceny postup pro velke ukoly

Pokud je ukol rozsahly, preferuj tento postup:

### Beh 1
- vytvor solution a projekty
- vytvor strukturu slozek a povinne soubory
- implementuj modely a sluzby
- priprav App.xaml, okna, views a viewmodely
- nastav appsettings.json
- udelej funkcni minimum tak, aby slo reseni buildnout
- priprav zaklad pro:
  - Zacatecnik
  - Pokrocily
  - Muj vysledek
  - Tridni vysledky
  - TeacherDashboard

### Beh 2
- dopln plnou funkcnost Zacatecnik
- dopln plnou funkcnost Pokrocily
- dopln Muj vysledek
- dopln Tridni vysledky
- dopln TeacherDashboard
- dopln exporty
- dopln sample data
- dopln README
- zachovej buildovatelnost

## Povinna architektura

Reseni musi pouzit presne tyto nazvy:
- `SchoolMathTrainer.sln`
- projekt `StudentApp`
- projekt `TeacherDashboard`
- projekt `SharedCore`

## Povinna struktura

- `SchoolMathTrainer.sln`

- `src/SharedCore/SharedCore.csproj`
- `src/SharedCore/Models/AppConfiguration.cs`
- `src/SharedCore/Models/StudentSummary.cs`
- `src/SharedCore/Models/StudentSession.cs`
- `src/SharedCore/Models/AnswerRecord.cs`
- `src/SharedCore/Models/StudentProgressSnapshot.cs`
- `src/SharedCore/Models/ClassOverviewItem.cs`
- `src/SharedCore/Models/LearningMode.cs`
- `src/SharedCore/Models/OperationType.cs`
- `src/SharedCore/Services/ConfigurationService.cs`
- `src/SharedCore/Services/FileSystemStorageService.cs`
- `src/SharedCore/Services/StatisticsService.cs`
- `src/SharedCore/Services/MathProblemGenerator.cs`
- `src/SharedCore/Services/RetryFileAccessService.cs`
- `src/SharedCore/Services/LoggingService.cs`
- `src/SharedCore/Services/CsvExportService.cs`
- `src/SharedCore/Services/StudentProgressService.cs`
- `src/SharedCore/Helpers/RelayCommand.cs`
- `src/SharedCore/Helpers/BaseViewModel.cs`

- `src/StudentApp/StudentApp.csproj`
- `src/StudentApp/App.xaml`
- `src/StudentApp/App.xaml.cs`
- `src/StudentApp/Views/StudentShellWindow.xaml`
- `src/StudentApp/Views/StudentShellWindow.xaml.cs`
- `src/StudentApp/Views/StudentLoginView.xaml`
- `src/StudentApp/Views/StudentLoginView.xaml.cs`
- `src/StudentApp/Views/BeginnerQuizView.xaml`
- `src/StudentApp/Views/BeginnerQuizView.xaml.cs`
- `src/StudentApp/Views/AdvancedDragDropView.xaml`
- `src/StudentApp/Views/AdvancedDragDropView.xaml.cs`
- `src/StudentApp/Views/MyResultsView.xaml`
- `src/StudentApp/Views/MyResultsView.xaml.cs`
- `src/StudentApp/Views/ClassResultsView.xaml`
- `src/StudentApp/Views/ClassResultsView.xaml.cs`
- `src/StudentApp/ViewModels/StudentShellViewModel.cs`
- `src/StudentApp/ViewModels/StudentLoginViewModel.cs`
- `src/StudentApp/ViewModels/BeginnerQuizViewModel.cs`
- `src/StudentApp/ViewModels/AdvancedDragDropViewModel.cs`
- `src/StudentApp/ViewModels/MyResultsViewModel.cs`
- `src/StudentApp/ViewModels/ClassResultsViewModel.cs`
- `src/StudentApp/Resources/Theme.xaml`
- `src/StudentApp/appsettings.json`

- `src/TeacherDashboard/TeacherDashboard.csproj`
- `src/TeacherDashboard/App.xaml`
- `src/TeacherDashboard/App.xaml.cs`
- `src/TeacherDashboard/Views/TeacherDashboardWindow.xaml`
- `src/TeacherDashboard/Views/TeacherDashboardWindow.xaml.cs`
- `src/TeacherDashboard/Views/StudentDetailView.xaml`
- `src/TeacherDashboard/Views/StudentDetailView.xaml.cs`
- `src/TeacherDashboard/ViewModels/TeacherDashboardViewModel.cs`
- `src/TeacherDashboard/ViewModels/StudentDetailViewModel.cs`
- `src/TeacherDashboard/Resources/Theme.xaml`
- `src/TeacherDashboard/appsettings.json`

- `sample-data/README.txt`
- `sample-data/Data/Students/`
- `sample-data/Data/Sessions/`

Pokud je potreba pridat dalsi soubory, pridej je, ale nemaz ani neprejmenovavej povinne soubory.

## Technologie

Pouzij:
- C#
- .NET WPF
- reseni buildovatelne ve Visual Studiu

Preferuj:
- ciste MVVM nebo stejne prehledne oddeleni logiky
- standardni WPF ovladaci prvky a postupy
- udrzovatelny kod

Vyhybej se zbytecnym externim knihovnam.

## Funkcni pozadavky

### StudentApp

StudentApp je pouze pro zaky.

Musi:
- umoznit prihlaseni jmenem, prezdivkou nebo ID
- neobsahovat admin funkce
- neobsahovat vstup do ucitelskeho rezimu
- nikdy neotevirat TeacherDashboard ze studentskeho UI

StudentApp musi obsahovat:
- prihlaseni zaka
- rezim Zacatecnik
- rezim Pokrocily
- pohled Muj vysledek
- pohled Tridni vysledky

### Zacatecnik

Kvizovy rezim:
- pouze scitani a odecitani
- rozsah vysledku jen 0 az 20
- zadne zaporne vysledky
- presne 4 moznosti odpovedi
- presne 1 spravna odpoved
- 3 spatne odpovedi musi byt verohodne a bez duplicit
- odpoved kliknutim nebo dotykem
- po vyhodnoceni automaticky dalsi priklad

### Pokrocily

Rezim drag and drop:
- bez hotovych tlacitek odpovedi
- zobrazi se jeden priklad
- zobrazi se cisla 0 az 20 jako pretahovatelne prvky
- zak pretahne spravne cislo do cilove oblasti
- musi fungovat drag and drop mysi
- UI musi byt jednoduche i pro dotyk
- po vyhodnoceni automaticky dalsi priklad

### Studentske UI

Musi byt:
- detske
- barevne
- citelne
- jednoduche
- s velkym pismem
- s velkymi ovladacimi prvky
- se zaoblenym vzhledem
- ne firemni nebo technicke

### Zpetna vazba

- spravna odpoved: kratka pochvala
- spatna odpoved: kratke povzbuzeni
- potom automaticky pokracovat

## Vysledkove mody

V produktu jsou 3 vysledkove mody:
- mod zak = `Muj vysledek`
- mod trida = `Tridni vysledky`
- mod ucitelka = `TeacherDashboard.exe`

### Muj vysledek

Zobraz pouze data aktualniho zaka:
- spravne odpovedi
- spatne odpovedi
- celkem odpovedi
- uspesnost v procentech
- vyvoj v case
- zlepseni oproti predchozim relacim
- zvlast statistiku pro Zacatecnik
- zvlast statistiku pro Pokrocily
- jednoduchy progress bar nebo jednoduchy graf

### Tridni vysledky

Sdileny tridni prehled:
- vsichni zaci
- jmeno nebo ID
- pocet vyresenych prikladu
- uspesnost
- trend zlepseni
- razeni podle uspesnosti, aktivity a poctu odpovedi
- bez admin funkci
- toto neni ucitelsky dashboard

### TeacherDashboard

Samostatna aplikace pouze pro ucitelku.

Musi:
- nacist data vsech zaku
- zobrazit prehlednou tabulku nebo seznam
- zobrazit jmeno nebo ID
- pocet odpovedi
- pocet spravnych
- pocet spatnych
- uspesnost
- pocet relaci
- cas posledni aktivity
- trend zlepseni
- vysledky Zacatecnik
- vysledky Pokrocily
- vyhledavani podle jmena nebo ID
- rucni refresh
- automaticky refresh

Detail zaka v TeacherDashboard:
- souhrn statistik
- relace
- detail odpovedi
- cas kazde odpovedi
- uspesnost po relacich
- posledni aktivita
- trend zlepseni v case
- srovnani Zacatecnik vs Pokrocily
- jednoduchy graf nebo progress bar, pokud to zbytecne nekomplikuje reseni

## Ulozeni dat

Pouzij pouze soubory ve sdilene slozce OneDrive.

Konfigurace cesty ke sdilene slozce musi byt v `appsettings.json`.

Pouzij JSON.

Neukladej vsechny zaky do jednoho souboru.

Povinna struktura sdilenych dat:
- `Data/Students/`
- `Data/Sessions/`
- `Data/Exports/`
- `Config/`
- `Logs/`

Kazdy zak musi mit:
- samostatny souhrnny JSON
- samostatne JSON soubory relaci

Po kazde odpovedi uloz minimalne:
- `studentId` nebo `studentName`
- `timestamp`
- `sessionId`
- `learningMode`
- `operationType`
- `exampleText`
- `offeredAnswers` jen pro Zacatecnik
- `chosenAnswer`
- `correctAnswer`
- `isCorrect`
- `inputMethod`
- `runningCorrectCount`
- `runningWrongCount`
- `runningTotalCount`
- `runningSuccessPercent`
- `lastActivityUtc`

## Odolnost

Musi byt osetreno:
- chybejici sdilena slozka
- nedostupna sdilena slozka
- poskozeny JSON
- docasne uzamcene soubory
- zpozdena synchronizace OneDrive

Pravidla:
- jeden rozbity soubor nesmi shodit celou aplikaci
- StudentApp musi pri chybe zapisu zobrazit srozumitelnou hlasku a provest bezpecny retry
- TeacherDashboard musi pokracovat, i kdyz nejde nacist jeden zak
- chyby se musi logovat

## Povinne modely

Implementuj minimalne:
- `AppConfiguration`
- `StudentSummary`
- `StudentSession`
- `AnswerRecord`
- `StudentProgressSnapshot`
- `ClassOverviewItem`
- `LearningMode`
- `OperationType`

## Povinne sluzby

Implementuj minimalne:
- `ConfigurationService`
- `FileSystemStorageService`
- `StatisticsService`
- `MathProblemGenerator`
- `RetryFileAccessService`
- `LoggingService`
- `CsvExportService`
- `StudentProgressService`

## Sample data

Vytvor sample data alespon pro 3 zaky a vice relaci:
- `sample-data/Data/Students/*.json`
- `sample-data/Data/Sessions/*.json`

## README

Pridat README s:
- jak otevrit solution
- jak buildnout projekt
- jak nastavit OneDrive cestu
- jak spustit StudentApp
- jak spustit TeacherDashboard

## Definice hotove prace

Ukol je hotovy az kdyz:
- existuji vsechny povinne soubory
- je implementovana veskera povinna funkcnost
- reseni je buildovatelne
- StudentApp funguje
- TeacherDashboard funguje
- existuji sample data
- existuje README
- nic mimo workspace nebylo zmeneno