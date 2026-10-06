using MessagePack;
using Newtonsoft.Json;

namespace AscNet.PcapParser
{
    [MessagePackObject(false)]
    public class Packet
    {
        [Key(0)]
        public int No;

        [Key(1)]
        public ContentType Type;

        [Key(2)]
        public byte[] Content;

        public enum ContentType
        {
            Request,
            Response,
            Push,
            Exception
        }

        [MessagePackObject(false)]
        public class Request
        {
            [Key(0)]
            public int Id;

            [Key(1)]
            public string Name;

            [Key(2)]
            public byte[] Content;

            public T Deserialize<T>()
            {
                return MessagePackSerializer.Deserialize<T>(Content);
            }

            public object Deserialize()
            {
                return DeserializeContent(Content);
            }
        }

        [MessagePackObject(false)]
        public class Response
        {
            [Key(0)]
            public int Id;

            [Key(1)]
            public string Name;

            [Key(2)]
            public byte[] Content;

            public T Deserialize<T>()
            {
                return MessagePackSerializer.Deserialize<T>(Content);
            }

            public object Deserialize()
            {
                return DeserializeContent(Content);
            }
        }

        [MessagePackObject(false)]
        public class Push
        {
            [Key(0)]
            public string Name;

            [Key(1)]
            public byte[] Content;

            public T Deserialize<T>()
            {
                return MessagePackSerializer.Deserialize<T>(Content);
            }

            public object Deserialize()
            {
                return DeserializeContent(Content);
            }
        }

        [MessagePackObject(false)]
        public class Exception
        {
            [Key(0)]
            public int Id;

            [Key(1)]
            public int Code;

            [Key(2)]
            public string Message;
        }

        private static object DeserializeContent(byte[] content)
        {
            try
            {
                string json = MessagePackSerializer.ConvertToJson(content);
                return JsonConvert.DeserializeObject(json) ?? new { };
            }
            catch (System.Exception ex)
            {
                return new
                {
                    error = ex.Message,
                    raw = System.Convert.ToBase64String(content)
                };
            }
        }
    }
}
