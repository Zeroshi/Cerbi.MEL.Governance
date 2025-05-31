using System;

namespace Cerbi
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class CerbiTopicAttribute : Attribute
    {
        public string TopicName { get; }
        public CerbiTopicAttribute(string topicName) => TopicName = topicName;
    }
}
