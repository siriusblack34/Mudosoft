@echo off
ECHO MudoSoft Backend Veritabanı Geri Çekme ve Güncelleme İşlemi Başlatılıyor...
ECHO --------------------------------------------------------------------

REM Ayarlarınızı kontrol edin (localhost ile başarılı bağlantı test edildi)
SET CONNECTION_STRING="Server=localhost;Database=MudosoftDev;Integrated Security=True;Encrypt=False;"

REM 1. Migration'ı geri çekme: Veritabanını en son bilinen (InitialCreate) noktaya çekiyoruz.
ECHO.
ECHO 1. Veritabanini Geri Cekme (Undo): Lütfen 'InitialCreate' yerine en son basarili migration adini kullanin!
ECHO.
dotnet ef database update InitialCreate --connection %CONNECTION_STRING%

IF ERRORLEVEL 1 (
    ECHO.
    ECHO ❌ HATA: Veritabanı geri çekilemedi. Bağlanti string'ini kontrol edin veya InitialCreate adini duzeltin.
    GOTO END
)
ECHO.
ECHO ✅ Veritabanı basariyla geri cekildi.

REM 2. Hatalı (veya fazla) Migration dosyasını silme.
ECHO.
ECHO 2. Mevcut Migration Dosyasini Kaldirma (AddCurrentMetricsToDevice)
ECHO.
dotnet ef migrations remove

IF ERRORLEVEL 1 (
    ECHO.
    ECHO ❌ HATA: Migration dosyasi kaldirilamadi.
    GOTO END
)
ECHO.
ECHO ✅ Migration dosyasi basariyla kaldirildi.


REM 3. Yeni ve Temiz Bir Migration Oluşturma
ECHO.
ECHO 3. Yeni, Temiz Migration Olusturuluyor...
ECHO.
dotnet ef migrations add FinalCurrentMetricsUpdate --connection %CONNECTION_STRING%

IF ERRORLEVEL 1 (
    ECHO.
    ECHO ❌ HATA: Yeni migration olusturulamadi. C# kodunuzda derleme hatasi olabilir.
    GOTO END
)
ECHO.
ECHO ✅ Yeni migration basariyla olusturuldu.

REM 4. Veritabanını Güncelleme (Final)
ECHO.
ECHO 4. Veritabanina Yeni Sutunlar Ekleniyor...
ECHO.
dotnet ef database update --connection %CONNECTION_STRING%

IF ERRORLEVEL 1 (
    ECHO.
    ECHO ❌ HATA: Veritabanina guncelleme uygulanamadi.
    GOTO END
)
ECHO.
ECHO ====================================================================
ECHO 🎉 BASARI: Veritabanı guncellendi. Backend'i calistirmaya hazirsiniz!
ECHO ====================================================================

:END
pause