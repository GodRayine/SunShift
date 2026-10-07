# Notes for certification — SunShift 0.5.0

Native WPF desktop application. No account, payment, advertisements, downloaded code,
generative AI service, or proprietary wallpaper catalogue. Source code is available
at https://github.com/GodRayine/SunShift under GPL-3.0-or-later.

The location capability is needed to determine the regional time zone and sunrise /
sunset. Windows Geolocator.RequestAccessAsync is called on the foreground UI thread
after the user enables automation or chooses Windows as the location source.
The UI explains the purpose. Location is kept in process memory only. The user may
select manual session coordinates or disable location use entirely. Denied location
access clears the former Windows position; no IP-location fallback is used.

runFullTrust is needed for this existing WPF / Win32 application, SystemParametersInfoW
desktop wallpaper changes, local file/folder selection and the tray icon. The app
runs as the current user and does not elevate or bypass system personalization policies.
The optional lock-screen change uses UserProfilePersonalizationSettings.
The optional startup task is disabled by default and controlled by Windows StartupTask.

Manual test steps (UI language is Russian):

1. Start SunShift. Use «День» and «Ночь» tabs and «Выбрать изображение» to select
   any local JPG / PNG / BMP. Lock-screen selection is optional.
2. Open «Источник и координаты…». Choose «Определять через Windows», then
   «Применить» and respond to the native Windows permission request. If unavailable,
   choose manual coordinates (for example 55.75, 37.61) for the current session.
3. Enable «Автоматическая смена обоев». The actual regional solar state chooses
   which profile applies. Editing the other profile does not change the current sun state.
4. Open «Коллекция обоев → Настроить коллекцию…». Choose separate folders or
   add pairs «день + ночь». The collection timer is a separate optional setting.
5. Deny location in Windows or choose «Не использовать местоположение». The
   former position is cleared and current wallpapers are preserved. Manual application
   using «Применить выбранный фон» remains available and pauses automation.
6. Enable / disable «Запускать с Windows». A Windows-disabled startup task is not
   re-enabled programmatically. Closing with X hides the app to the tray;
   «Выйти» fully shuts it down.
7. Fully exit and restart: Windows location is acquired again when automatic mode
   is enabled; manual coordinates must be entered again. JSON contains no coordinates.

Lock-screen support depends on Windows personalization configuration / device policies.
The app displays API rejection and keeps desktop success separate. It never rewrites
an unspecified screen. Runtime and geotimezone data are bundled; solar calculations
do not depend on a SunShift server. Windows location services may use the network.

The command-line preview / integration modes use isolated demo profiles and never
change actual system wallpapers or request real location. These are regression tools,
not the app's normal runtime. Store screenshots show these actual application windows.
