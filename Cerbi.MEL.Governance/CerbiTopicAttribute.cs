using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cerbi
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class CerbiTopicAttribute : Attribute
    {
        public string TopicName { get; }
        public CerbiTopicAttribute(string topicName) => TopicName = topicName;
    }
}
