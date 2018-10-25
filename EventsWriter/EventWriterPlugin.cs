using Neo.IO.Data.LevelDB;

namespace Neo.Plugins
{
    public class EventWriterPlugin : Plugin
    {
        public override string Name => "EventWriterPlugin";

        public EventWriterPlugin()
        {
            System.ActorSystem.ActorOf(EventsWriter.Props(System.Blockchain));
        }
    }
}