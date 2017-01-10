using System;
using log4net;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Xbim.Common.Enumerations;
using Xbim.Common.ExpressValidation;
using Xbim.Ifc4.Interfaces;
using static Xbim.Ifc4.Functions;
// ReSharper disable once CheckNamespace
// ReSharper disable InconsistentNaming
namespace Xbim.Ifc4.StructuralElementsDomain
{
	public partial class IfcSurfaceFeature : IExpressValidatable
	{
		public enum IfcSurfaceFeatureClause
		{
			HasObjectType,
		}

		/// <summary>
		/// Tests the express where-clause specified in param 'clause'
		/// </summary>
		/// <param name="clause">The express clause to test</param>
		/// <returns>true if the clause is satisfied.</returns>
		public bool ValidateClause(IfcSurfaceFeatureClause clause) {
			var retVal = false;
			try
			{
				switch (clause)
				{
					case IfcSurfaceFeatureClause.HasObjectType:
						retVal = !EXISTS(PredefinedType) || (PredefinedType != IfcSurfaceFeatureTypeEnum.USERDEFINED) || EXISTS(this/* as IfcObject*/.ObjectType);
						break;
				}
			} catch (Exception ex) {
				var Log = LogManager.GetLogger("Xbim.Ifc4.StructuralElementsDomain.IfcSurfaceFeature");
				Log.Error(string.Format("Exception thrown evaluating where-clause 'IfcSurfaceFeature.{0}' for #{1}.", clause,EntityLabel), ex);
			}
			return retVal;
		}

		public override IEnumerable<ValidationResult> Validate()
		{
			foreach (var value in base.Validate())
			{
				yield return value;
			}
			if (!ValidateClause(IfcSurfaceFeatureClause.HasObjectType))
				yield return new ValidationResult() { Item = this, IssueSource = "IfcSurfaceFeature.HasObjectType", IssueType = ValidationFlags.EntityWhereClauses };
		}
	}
}