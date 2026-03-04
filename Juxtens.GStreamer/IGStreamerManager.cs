using Juxtens.DeviceManager;

namespace Juxtens.GStreamer;

public interface IGStreamerManager
{
    Result<StreamHandle, GstError> StartSender(SenderConfig config);
    Result<StreamHandle, GstError> StartReceiver(ReceiverConfig config);
}
