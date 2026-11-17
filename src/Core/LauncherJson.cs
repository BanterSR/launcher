using System.Text.Json;
using System.Text.Json.Serialization;
using Momoko.Launcher.App;
using Momoko.Launcher.Games;
using Momoko.Launcher.Games.Gakumas;
using Momoko.Launcher.Services;

namespace Momoko.Launcher.Core;

// Source-generated JSON metadata for every serialized type, so the user build
// needs no reflection under NativeAOT.
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,
    NumberHandling=JsonNumberHandling.AllowReadingFromString,
    DefaultIgnoreCondition=JsonIgnoreCondition.Never,
    WriteIndented=true)]
[JsonSerializable(typeof(LauncherSettings))]
[JsonSerializable(typeof(SessionMarker))]
[JsonSerializable(typeof(ProxyInstallRecord))]
[JsonSerializable(typeof(LocalizationInfo))]
[JsonSerializable(typeof(MomokoEnvelope<MomokoTokens>))]
[JsonSerializable(typeof(MomokoEnvelope<MomokoClientAuthorization>))]
[JsonSerializable(typeof(MomokoEnvelope<MomokoMe>))]
[JsonSerializable(typeof(MomokoEnvelope<MomokoLaunchApps>))]
[JsonSerializable(typeof(MomokoEnvelope<MomokoLink>))]
[JsonSerializable(typeof(MomokoEnvelope<MomokoTicket>))]
[JsonSerializable(typeof(MomokoEnvelope<MomokoLauncherConfig>))]
[JsonSerializable(typeof(MomokoEnvelope<JsonElement>))]
[JsonSerializable(typeof(MomokoError))]
[JsonSerializable(typeof(MomokoEmpty))]
[JsonSerializable(typeof(MomokoClientAuthRequest))]
[JsonSerializable(typeof(MomokoClientTokenRequest))]
[JsonSerializable(typeof(MomokoRefreshRequest))]
[JsonSerializable(typeof(MomokoLaunchBindRequest))]
[JsonSerializable(typeof(SdkTicketRequest))]
#if DEV
[JsonSerializable(typeof(SdkPasswordRequest))]
#endif
[JsonSerializable(typeof(SdkSessionReply))]
[JsonSerializable(typeof(SdkErrorReply))]
[JsonSerializable(typeof(UiState))]
[JsonSerializable(typeof(UiLog))]
[JsonSerializable(typeof(UiToast))]
internal sealed partial class LauncherJson:JsonSerializerContext;
