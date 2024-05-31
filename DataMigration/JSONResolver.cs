using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DataMigration
{

    public class IgnoreVirtualPropertiesContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            // Ignore properties marked with the 'virtual' keyword
            if (IsVirtualProperty(member))
            {
                property.ShouldSerialize = instance => false;
            }

            return property;
        }

        private bool IsVirtualProperty(MemberInfo member)
        {
            
            if (member is PropertyInfo propertyInfo)
            {
                if(member.DeclaringType.Name== "QB_Tm_VirtualWalletLogs")
                {

                }
                if  (propertyInfo.PropertyType == typeof(string) )
                {
                    return false;
                }else if (propertyInfo.PropertyType == typeof(int) || propertyInfo.PropertyType == typeof(int?))
                {
                    return false;
                }else if (propertyInfo.GetMethod?.IsVirtual??false)
                {
                    return true;
                }
                else
                {
                    return false;
                }
                
            }
            return false;
        }

    }
}
