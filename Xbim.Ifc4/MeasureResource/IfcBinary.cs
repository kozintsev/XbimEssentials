// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool Xbim.CodeGeneration 
//  
//     Changes to this file may cause incorrect behaviour and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------

using Xbim.Common;
using Xbim.Common.Exceptions;

namespace Xbim.Ifc4.MeasureResource
{
	[ExpressType("IfcBinary", 986)]
	[DefinedType(typeof(string))]
    // ReSharper disable once PartialTypeWithSinglePart
	public partial struct IfcBinary : IExpressValueType, IExpressBinaryType, System.IEquatable<string>
	{ 
		private string _value;
        
		public object Value
        {
            get { return _value; }
        }

 
		string IExpressBinaryType.Value { get { return _value; } }

		public override string ToString()
        {
			return _value ?? "";
        }
        public IfcBinary(string val)
        {
            _value = val;
        }


        public static implicit operator IfcBinary(string value)
        {
            return new IfcBinary(value);
        }

        public static implicit operator string(IfcBinary obj)
        {
            return obj._value;

        }


        public override bool Equals(object obj)
        {
			if (obj == null && Value == null)
                return true;

            if (obj == null)
                return false;

            if (GetType() != obj.GetType())
                return false;

            return ((IfcBinary) obj)._value == _value;
        }

		public bool Equals(string other)
	    {
	        return this == other;
	    }

        public static bool operator ==(IfcBinary obj1, IfcBinary obj2)
        {
            return Equals(obj1, obj2);
        }

        public static bool operator !=(IfcBinary obj1, IfcBinary obj2)
        {
            return !Equals(obj1, obj2);
        }

        public override int GetHashCode()
        {
            return Value != null ? _value.GetHashCode() : base.GetHashCode();
        }

		#region IPersist implementation
		void IPersist.Parse(int propIndex, IPropertyValue value, int[] nestedIndex)
		{
			if (propIndex != 0)
				throw new XbimParserException(string.Format("Attribute index {0} is out of range for {1}", propIndex + 1, GetType().Name.ToUpper()));
            _value = value.HexadecimalVal;
            
		}
		#endregion

		#region IExpressValueType implementation
        System.Type IExpressValueType.UnderlyingSystemType { 
			get 
			{
				return typeof(string);
			}
		}
		#endregion


	}
}
