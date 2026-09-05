# College Schedule Gadget

WPF-віджет розкладу для Windows. Дані завантажуються з сайту ВСП «Фаховий коледж електронних приладів ІФНТУНГ».

## Можливості

- розклад вибраної групи на поточний або вибраний день;
- автопідказки груп;
- відлік до наступної пари;
- теми, стилі картки та прозорість;
- збереження групи, позиції, розміру й налаштувань;
- закріплення віджета та запуск разом із Windows;
- автоматична перевірка GitHub Releases.

## Запуск для користувачів

Завантажте `CollegeScheduleGadget.exe` зі сторінки [Releases](https://github.com/Deniska2010/rorklad/releases), розмістіть його в окремій папці та запустіть подвійним кліком.

EXE публікується як self-contained для Windows x64, тому окремий .NET runtime не потрібен.

## Збірка

Потрібен .NET 10 SDK для Windows:

```powershell
dotnet build CollegeScheduleGadget.csproj -c Release
```

Щоб отримати один standalone EXE:

```powershell
dotnet publish CollegeScheduleGadget.csproj -c Release -r win-x64 --self-contained true -o publish
```

Готовий файл буде у `publish/CollegeScheduleGadget.exe`.

## Реліз оновлення

1. Збільште `CurrentVersion` у `UpdateService.cs`.
2. Опублікуйте нову збірку через `dotnet publish`.
3. Створіть GitHub Release з тегом, наприклад `v1.0.1`.
4. Додайте до Release `CollegeScheduleGadget.exe`.

Віджет перевіряє останній реліз репозиторію `Deniska2010/rorklad` і показує кнопку оновлення, якщо тег новіший за локальну версію.
