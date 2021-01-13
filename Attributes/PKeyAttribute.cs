using System;

namespace Design_Patterns_project.Attributes

{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    sealed class PKeyAttribute : Attribute
    {
        public string m_pKeyName{private set; get;}
        
        public PKeyAttribute(){}
    }
}