# College Schedule Gadget: контекст для наступного ШІ

## Проєкт

WPF desktop-віджет для Windows, який завантажує розклад із:
`https://kep.nung.edu.ua/pages/education/schedule`

Target: `net10.0-windows`, `win-x64`, self-contained single-file EXE.

## Основні файли

- `MainWindow.xaml` — картка віджета, розклад, кнопки закриття/pin/оновлення/налаштувань, навігація днями, clock і empty state вихідних.
- `MainWindow.xaml.cs` — завантаження даних, тижні, відлік, системні сповіщення, теми, розміри, pin, позиція, single-window settings.
- `SettingsWindow.xaml` — окреме стандартне Windows-вікно налаштувань із лівою навігацією.
- `SettingsWindow.xaml.cs` — apply без закриття, група, прозорість, розмір тексту, стилі, власний колір, оновлення.
- `ScheduleService.cs` — парсинг `scheduleData`, груп, повних імен викладачів і `DEFAULT_WEEK_CYCLES` із сайту.
- `AppSettings.cs` — JSON-модель налаштувань.
- `StartupManager.cs` — запуск разом із Windows через `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- `UpdateService.cs` — перевірка GitHub Releases API для `Deniska2010/rorklad`.
- `App.xaml.cs` — mutex для одного активного екземпляра.
- `hardcore-heart.png` — вбудований ресурс для tray notification icon.

## Налаштування

Зберігаються в:
`%LocalAppData%\CollegeScheduleGadget\settings.json`

Модель має:

- `Group` — група;
- `Opacity` — 0.25..1.0;
- `Left`, `Top`, `Width`, `Height` — позиція/розмір;
- `IsPinned` — блокування перетягування і resize;
- `StartWithWindows` — автозапуск;
- `Theme` — вбудовані теми (`Midnight`, `Violet`, `Ember`, `Graphite`);
- `WidgetStyle` — `Minimalism` або `Cloud`;
- `TextSize` — `Small`, `Medium`, `Large`;
- `CustomColor` — власний HEX-колір картки.

`Зберегти` в settings застосовує зміни через `SettingsApplied`, але не закриває settings window. Вікно налаштувань відкривається немодально й повторне натискання шестерні активує вже відкрите вікно.

## Тижні

`ScheduleService.LoadWeekCyclesAsync()` читає з HTML сайту `DEFAULT_WEEK_CYCLES`, наприклад:

- 1: `01.09.2026`;
- 2: `07.09.2026`;
- 3: `14.09.2026`;
- 4: `21.09.2026`.

`MainWindow` обирає номер тижня для вибраного дня, а не лише для поточної дати. Якщо сайт недоступний, використовується fallback-розрахунок.

## Викладачі

Повні ПІБ завантажуються з:
`https://kep.nung.edu.ua/api/exam-hints`

У розкладі можуть бути скорочені імена; при наведенні на викладача показується округлений themed tooltip із повним іменем, якщо ключ знайдено.

## Сповіщення

- використовується `System.Windows.Forms.NotifyIcon`;
- вбудований `hardcore-heart.png` перетворюється на tray icon;
- за 3 хвилини до пари показується balloon один раз на дату+номер пари;
- системні сповіщення залежать від Windows Notifications/Focus Assist.

## Оновлення

`UpdateService` перевіряє:
`https://api.github.com/repos/Deniska2010/rorklad/releases/latest`

Поточна версія задається в `UpdateService.CurrentVersion`.
Для релізу потрібно:

1. збільшити версію;
2. виконати publish;
3. створити GitHub Release з тегом `vX.Y.Z`;
4. прикріпити `CollegeScheduleGadget.exe`.

Кнопка оновлення в головному віджеті показується лише якщо GitHub повернув новіший тег. Вона відкриває asset або сторінку релізу; повної автоматичної заміни EXE поки немає.

## Збірка

```powershell
dotnet build CollegeScheduleGadget.csproj -c Release
dotnet publish CollegeScheduleGadget.csproj -c Release -r win-x64 --self-contained true -o publish
```

Готовий файл:
`publish\CollegeScheduleGadget.exe`

## GitHub

Remote:
`https://github.com/Deniska2010/rorklad.git`

Папки `bin`, `obj`, `publish` і сертифікати ігноруються через `.gitignore`.

## Поточні важливі рішення

- Не використовувати `Topmost=true` постійно: віджет має бути видимим на desktop, але не перекривати інші програми.
- Не повертати `ShowDialog()` для settings: це закриває settings після `DialogResult`; поточна модель використовує `Show()` + `SettingsApplied`.
- Підключений WinForms вимагає явних WPF-кваліфікаторів (`System.Windows.Media.Color`, `System.Windows.Application` тощо), бо виникають конфлікти типів.
- Якщо language server VS Code показує старі помилки XAML-імен, перевіряти `dotnet build`; generated XAML diagnostics можуть оновлюватися із затримкою.
- Повного автоматичного захисту EXE від копіювання немає; для довіри Windows потрібен Authenticode Code Signing Certificate.
