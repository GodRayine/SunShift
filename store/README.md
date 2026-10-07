# Подготовка SunShift к Microsoft Store

Версия 0.5.0, x64. Получены идентификаторы продукта из Partner Center для сборки
пакета Store. WACK 10.0.28000.2957 проверил файлы Store MSIX: итог PASS,
13 из 13 обязательных тестов пройдены. Также подготовлены тестовый MSIX и материалы;
публикации и сертификата IARC пока нет. Проверка установленного приложения остаётся.

## Готовые материалы

- `listing.ru.md`: описание, возможности, ссылки, подписи скриншотов.
- `age-rating.md`: факты для анкеты IARC с целью «для всех возрастов».
- `certification-notes.md`: назначение разрешений и сценарии для проверки Microsoft.
- `validation.md`: выполненные проверки и оставшиеся проверки на устройстве.
- `wack-2026-10-07.md`: итог WACK и разбор дополнительного замечания.
- `../PRIVACY.md`: политика, доступная также внутри приложения.
- `../artifacts/store-materials/screenshots`: шесть PNG 1920×1080.
- `../artifacts/store-materials/icons`: иконки, включая рекомендованную 300×300.
- `../artifacts/SunShift-v0.5.0-store-x64.msix`: пакет с identity продукта,
  без локальной подписи; Microsoft Store подписывает его при публикации.
- `../artifacts/SunShift-v0.5.0-development-x64.msix`: тестовая identity,
  тестовая подпись; это не пакет для загрузки в карточку Store.
- `../artifacts/SunShift-development.cer`: открытая часть тестового сертификата,
  срок действия до 6 ноября 2026. Закрытый ключ не поставляется.

## Сборка и критерии

1. Координаты живут только в сеансе; миграция очищает настройки и копии.
2. Доступ через стандартное разрешение Windows; отключение / отказ забывает позицию.
3. MSIX содержит location и runFullTrust и проходит MakeAppx без обхода валидации.
4. MSIX использует StartupTask; режим portable сохраняет собственный автозапуск.
5. Все тесты проходят, снимки соответствуют настоящему интерфейсу и требованиям Store.
6. Перед отправкой: настоящая identity, установка / удаление, WACK, проверка на целевой
   Windows, публичная политика и анкета IARC. Эти шаги не заменяются MakeAppx.

Требуется .NET 10 SDK. MakeAppx / SignTool можно получить из официального NuGet
Microsoft.Windows.SDK.BuildTools: `Get-SdkTools.ps1` фиксирует версию и SHA-256.
Для тестового пакета:

```powershell
./scripts/Publish-Msix.ps1 -Development
./scripts/Sign-DevelopmentMsix.ps1
```

Скрипт создаёт новый staging и отказывается повторно использовать старый, чтобы
устаревшие DLL не попали в пакет. Перед повторной сборкой переместите предыдущую
папку `artifacts/msix-development-v0.5.0` за пределы staging; исходники не удаляйте.
Пакет после сборки не подписан. `Sign-DevelopmentMsix.ps1` создаёт временный
сертификат с Subject `CN=SunShift Development`, подписывает только development
пакет и экспортирует только `.cer`, не меняя доверие Windows.
Уже подготовленный здесь пакет подписан отдельным временным сертификатом.

Чтобы установить подготовленный пакет, запустите **Windows PowerShell от администратора
той же учётной записи** и выполните:

```powershell
./scripts/Install-DevelopmentMsix.ps1
```

Скрипт проверит совпадение подписи с открытым сертификатом и установит его именно в
LocalMachine/TrustedPeople, а не Trusted Root. После проверки удалите тестовый пакет
через «Параметры → Приложения», а временный сертификат из TrustedPeople, если он больше
не нужен. Файлы staging не являются установленным пакетом; прямой запуск EXE из них
не проверяет package identity.

## Пакет для продукта в Partner Center

Идентификаторы, предоставленные владельцем 7 октября 2026:

| Поле | Значение |
| --- | --- |
| Package/Identity/Name | `Gordry.SunShift` |
| Package/Identity/Publisher | `CN=1A375509-6CC3-4D47-8835-59909F491549` |
| Package/Properties/PublisherDisplayName | `Gordry` |

Команда повторной сборки:

```powershell
./scripts/Publish-Msix.ps1 -IdentityName 'Gordry.SunShift' -Publisher 'CN=1A375509-6CC3-4D47-8835-59909F491549' -PublisherDisplayName 'Gordry'
```

Сохраняйте точный регистр идентификаторов. Перед повторной сборкой переместите
предыдущую папку `artifacts/msix-store-v0.5.0` за пределы staging.
Получится `SunShift-v0.5.0-store-x64.msix`. Store сам подписывает MSIX при публикации;
покупка сертификата для этой подачи не требуется. Для локальной проверки пакета
с настоящей identity нужен отдельный соответствующий тестовый сертификат:
скрипты `Sign-DevelopmentMsix.ps1` и `Install-DevelopmentMsix.ps1` предназначены
только для пакета `SunShift.Development`.

Windows App Certification Kit 10.0.28000.2957 установлен на компьютере подготовки.
На другом компьютере установите компонент **Windows App Certification Kit** из
официального Windows SDK (идентификатор компонента установщика:
`OptionId.WindowsSoftwareLogoToolkit`). Для проверки файлов пакета запустите
из Windows PowerShell от администратора:

```powershell
./scripts/Run-Wack.ps1 -PackagePath ./artifacts/SunShift-v0.5.0-store-x64.msix -ReportPath ./artifacts/store-checks/wack-store.xml
```

Скрипт требует новый путь отчёта, чтобы старый результат не приняли за новую проверку.
Этот режим проверяет файлы MSIX и не подтверждает успешную установку и поведение
приложения. Для проверки с именем уже установленного пакета:

```powershell
./scripts/Run-Wack.ps1 -PackageFullName 'ИМЯ_УСТАНОВЛЕННОГО_ПАКЕТА'
```

После прохождения WACK выполните оставшиеся сценарии из `validation.md`. В карточке
заявляйте только проверенные Windows / архитектуры; ARM64 здесь не упакован.
Загрузите пакет, русский текст и снимки, внесите ссылку на PRIVACY.md, заполните
анкету IARC и ограниченные разрешения по `certification-notes.md`, отправьте на проверку.
Доступ к учётной записи и принятие соглашений разработчика выполняются владельцем.

## ИИ и правила

SunShift не вызывает модель ИИ и не создаёт контент в ответ на пользовательский ввод.
Помощь ИИ при написании кода не подпадает под раздел 11.16 о **live generative AI**.
Если позже добавить генерацию обоев моделью, потребуются раскрытие этой функции в
карточке / Partner Center и механизм жалоб на контент. Общие правила Store и права
на код / изображения применяются в любом случае.

Проверены [правила 7.19](https://learn.microsoft.com/en-us/windows/apps/publish/store-policy-archive/store-policy-7-19)
(действуют 7 октября 2026) и [7.20](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)
(вступают 22 октября 2026). Раздел 11.16 относится к генерации во время использования.
Отсутствие записи координат не отменяет политику конфиденциальности для Win32.

[Скриншоты и изображения MSIX](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)
и [подпись MSIX](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide).
