# Сборка из исходников

## Требования

- .NET SDK 9.0.200 или более новый SDK ветки 9.0; `global.json` разрешает более новые feature bands этой ветки.
- Android workload для .NET 9, Android SDK с платформой API 35 и build-tools, JDK 21.
- PowerShell 7 для скрипта сборки. Android Studio как редактор не обязателен.

```powershell
git clone https://github.com/xxXRatJuiceXxx/YandexHUD-TEYES.git
cd YandexHUD-TEYES
dotnet workload install android
```

Задайте `ANDROID_HOME` и `JAVA_HOME` или передайте каталоги явно:

```powershell
./scripts/build-headunit.ps1 -AndroidSdk 'C:/Android/Sdk' -JavaSdk 'C:/Java/jdk-21'
```

Скрипт запускает две группы проверок, собирает Release APK для `arm64-v8a` и `armeabi-v7a`, подписывает и сохраняет результат в `artifacts/release/`. Приложение использует `net9.0-android`, API 35 и минимальный API 26; распознавание кадра отдельно проверяет наличие API 30+.

## Подпись

При самостоятельной сборке используется локальный Android debug-ключ разработчика. Для существующего ключа с обычными Android debug-параметрами можно передать `-SigningKey '/path/to/debug.keystore'`.

Опубликованный APK 1.4 подписан тем же исходным Android debug-ключом, что использовался в предыдущих частных версиях. Ключ не включён в репозиторий. Сборка с другим ключом не устанавливается поверх опубликованного APK с тем же идентификатором; для независимого форка выберите свой `ApplicationId` или учитывайте необходимость переустановки и потери локальных настроек.

SHA-256 сертификата опубликованного APK:

```text
6e3ad39a8de929c37db5a92c871f7c82c7ee73aa94c1c3a38752d77e3d2c64de
```

SHA-256 **самого APK 1.4**:

```text
9ee2f358a698ec120cc8dea0f47b59ee41999bd8ec6704de8d0fd1b2f94d66ce
```

Исходные файлы приложения взяты из сохранённого комплекта 1.4. Побайтовое совпадение самостоятельно собранного APK не обещается: на результат влияют SDK, workload и ключ подписи.

## Проверки без Android

```powershell
dotnet run --project tests/WiiYiiHudNavigator.Yandex.Tests -c Release
dotnet run --project tests/YandexHudHeadUnit.Tests -c Release
```

Android workload для этих двух консольных проектов не нужен. Эти же проверки выполняются в GitHub Actions.

Дополнительные Android fixtures включаются только в Debug. Для них нужен установленный Яндекс Навигатор с подходящими ресурсами. На тестовом устройстве можно запустить:

```text
adb shell am start -n com.avashield.yandexhud.headunit/.MainActivity --ez reader_self_test true
adb logcat -d -s YandexCameraTest:I
```

Debug-режимы `probe_navigation` и `probe_image_only` предназначены для локальной проверки разработчиком: первый записывает состояния навигации в приватный cache приложения, второй игнорирует описание стрелки для проверки изображения. Не публикуйте полученные данные поездок. В Release эти команды исключены.

## Структура

| Каталог | Содержание |
| --- | --- |
| `src/YandexHudHeadUnit` | Android-интерфейс, BLE, восстановление соединения, получение экранного кадра |
| `src/YandexNotificationBridge` | Общий код чтения элементов/уведомлений Яндекса, распознавания и выбора актуальных данных |
| `src/WiiYiiHudNavigator.Plugin.Yandex` | Преобразование данных, текст HUD и код, используемый регрессионными проверками |
| `src/WiiYiiHudNavigator.Common` | Общие модели и интерфейс HUD из исходного проекта |
| `tests` | Две автономные группы проверок .NET |

Основной проект подключает общие `.cs` файлы напрямую и определяет `HEADUNIT`. Названия каталогов сохранены из истории разработки, но старые самостоятельные проекты телефонного моста и MAUI-плагина здесь не собираются.
