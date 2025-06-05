using Cerbi;
using System;
using Xunit;

namespace Cerbi.Tests
{
    public class CerbiTopicAttributeTests
    {
        [Fact]
        public void CerbiTopicAttribute_StoresTopicName()
        {
            var attr = new CerbiTopicAttribute("Orders");
            Assert.Equal("Orders", attr.TopicName);
        }

        [Fact]
        public void Attribute_Applies_To_Class()
        {
            // Use reflection to confirm that the attribute can be placed on a class
            var typeWithAttr = typeof(SampleClass);
            var found = Attribute.GetCustomAttribute(typeWithAttr, typeof(CerbiTopicAttribute))
                        as CerbiTopicAttribute;
            Assert.NotNull(found);
            Assert.Equal("Invoices", found.TopicName);
        }

        [CerbiTopic("Invoices")]
        private class SampleClass { }
    }
}