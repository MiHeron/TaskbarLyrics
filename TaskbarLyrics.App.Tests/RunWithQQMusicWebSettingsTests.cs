using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

[Collection("FloatingPanelInput")]
public sealed class RunWithQQMusicWebSettingsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SettingsMessageUpdatesAndSerializesRunWithQQMusic(bool enabled)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                using var offsets = new TrackLyricOffsetStore();
                var window = new SettingsWindow(settings, offsets,
                    () => Task.FromResult<CurrentTrackLyricsContext?>(null), () => throw new NotSupportedException(),
                    (_, _, _) => throw new NotSupportedException(), (_, _, _) => throw new NotSupportedException(), () => { });
                try
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    void Update(object value)
                    {
                        var message = SettingsWebMessageRouter.Parse(JsonSerializer.Serialize(new
                        {
                            version = 1,
                            type = "update",
                            payload = new { key = "runWithQQMusic", value }
                        }));
                        Assert.NotNull(message);
                        typeof(SettingsWindow).GetMethod("ApplyWebSettingUpdate", flags)!.Invoke(window, [message.Key, message.Value]);
                    }

                    Update("invalid");
                    Assert.False(settings.RunWithQQMusic);
                    Update(enabled);
                    Assert.Equal(enabled, settings.RunWithQQMusic);

                    var dto = typeof(SettingsWindow).GetMethod("CreateSettingsPayload", flags)!.Invoke(window, null)!;
                    var payload = JsonSerializer.SerializeToElement(dto, SettingsWebJson.Options);
                    Assert.Equal(enabled, payload.GetProperty("runWithQQMusic").GetBoolean());
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Settings interaction did not finish.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
