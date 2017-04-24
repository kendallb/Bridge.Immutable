using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ProductiveRage.Immutable.Analyser
{
	[DiagnosticAnalyzer(LanguageNames.CSharp)]
	public class PropertyIdentifierAttributeAnalyzer : DiagnosticAnalyzer
	{
		public const string DiagnosticId = "PropertyIdentifierAttribute";
		public const string Category = "Design";
		public static DiagnosticDescriptor ArgumentMustBeTwoArgumentDelegateRule = new DiagnosticDescriptor(
			DiagnosticId,
			GetLocalizableString(nameof(Resources.PropertyIdentifierAttributeAnalyserTitle)),
			GetLocalizableString(nameof(Resources.PropertyIdentifierAttributeInvalidDelegateMessageFormat)),
			Category,
			DiagnosticSeverity.Error,
			isEnabledByDefault: true
		);
		public static DiagnosticDescriptor NoReassignmentRule = new DiagnosticDescriptor(
			DiagnosticId,
			GetLocalizableString(nameof(Resources.PropertyIdentifierAttributeAnalyserTitle)),
			GetLocalizableString(nameof(Resources.PropertyIdentifierAttributeReassignmentMessageFormat)),
			Category,
			DiagnosticSeverity.Error,
			isEnabledByDefault: true
		);
		
		public static DiagnosticDescriptor SimplePropertyAccessorArgumentAccessRule = new DiagnosticDescriptor(
			DiagnosticId,
			GetLocalizableString(nameof(Resources.PropertyIdentifierAttributeAnalyserTitle)),
			GetLocalizableString(nameof(Resources.PropertyIdentifierAttributeSimplePropertyAccessorArgumentMessageFormat)),
			Category,
			DiagnosticSeverity.Error,
			isEnabledByDefault: true
		);
		public static DiagnosticDescriptor IndirectTargetAccessorAccessRule = new DiagnosticDescriptor(
			DiagnosticId,
			GetLocalizableString(nameof(Resources.PropertyIdentifierAttributeAnalyserTitle)),
			GetLocalizableString(nameof(Resources.PropertyIdentifierAttributeDirectPropertyTargetAccessorArgumentMessageFormat)),
			Category,
			DiagnosticSeverity.Error,
			isEnabledByDefault: true
		);
		public static DiagnosticDescriptor BridgeAttributeAccessRule = new DiagnosticDescriptor(
			DiagnosticId,
			GetLocalizableString(nameof(Resources.PropertyIdentifierAttributeAnalyserTitle)),
			GetLocalizableString(nameof(Resources.PropertyIdentifierAttributeBridgeAttributeMessageFormat)),
			Category,
			DiagnosticSeverity.Error,
			isEnabledByDefault: true
		);
		public static DiagnosticDescriptor PropertyMayNotBeSetToInstanceOfLessSpecificTypeRule = new DiagnosticDescriptor(
			DiagnosticId,
			GetLocalizableString(nameof(Resources.PropertyIdentifierAttributeAnalyserTitle)),
			GetLocalizableString(nameof(Resources.PropertyIdentifierAttributeTargetTypeNotSpecificEnoughMessageFormat)),
			Category,
			DiagnosticSeverity.Error,
			isEnabledByDefault: true
		);

		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
		{
			get
			{
				return ImmutableArray.Create(
					ArgumentMustBeTwoArgumentDelegateRule,
					NoReassignmentRule,
					SimplePropertyAccessorArgumentAccessRule,
					IndirectTargetAccessorAccessRule,
					BridgeAttributeAccessRule,
					PropertyMayNotBeSetToInstanceOfLessSpecificTypeRule
				);
			}
		}

		public override void Initialize(AnalysisContext context)
		{
			context.RegisterSyntaxNodeAction(LookForIllegalPropertyAttributeIdentifierSpecification, SyntaxKind.InvocationExpression);
			context.RegisterSyntaxNodeAction(LookForPropertyIdentifierReassignment, SyntaxKind.SimpleAssignmentExpression);
		}

		private void LookForIllegalPropertyAttributeIdentifierSpecification(SyntaxNodeAnalysisContext context)
		{
			var invocation = context.Node as InvocationExpressionSyntax;
			if (invocation == null)
				return;

			var method = context.SemanticModel.GetSymbolInfo(invocation.Expression).Symbol as IMethodSymbol;
			if (method == null)
				return;

			var invocationArgumentDetails = method.Parameters
				.Select((p, i) => new
				{
					Index = i,
					Parameter = p,
					HasPropertyIdentifierAttribute = CommonAnalyser.HasPropertyIdentifierAttribute(p)
				});

			// Look for argument values passed to methods where the method argument is identified as [PropertyIdentifier] - we need to ensure that these meet the usual With / CtorSet / GetProperty criteria
			foreach (var propertyIdentifierArgumentDetails in invocationArgumentDetails.Where(a => a.HasPropertyIdentifierAttribute))
			{
				var argumentValue = invocation.ArgumentList.Arguments[propertyIdentifierArgumentDetails.Index];
				var parameterTypeNamedSymbol = propertyIdentifierArgumentDetails.Parameter.Type as INamedTypeSymbol;
				if ((parameterTypeNamedSymbol == null)
				|| (parameterTypeNamedSymbol.DelegateInvokeMethod == null)
				|| (parameterTypeNamedSymbol.DelegateInvokeMethod.ReturnsVoid))
				{
					context.ReportDiagnostic(Diagnostic.Create(
						ArgumentMustBeTwoArgumentDelegateRule,
						argumentValue.GetLocation()
					));
					continue;
				}

				IPropertySymbol propertyIfSuccessfullyRetrieved;
				switch (CommonAnalyser.GetPropertyRetrieverArgumentStatus(argumentValue, context, propertyValueTypeIfKnown: parameterTypeNamedSymbol.DelegateInvokeMethod.ReturnType, propertyIfSuccessfullyRetrieved: out propertyIfSuccessfullyRetrieved))
				{
					case CommonAnalyser.PropertyValidationResult.Ok:
					case CommonAnalyser.PropertyValidationResult.UnableToConfirmOrDeny:
						continue;

					case CommonAnalyser.PropertyValidationResult.IndirectTargetAccess:
						context.ReportDiagnostic(Diagnostic.Create(
							IndirectTargetAccessorAccessRule,
							argumentValue.GetLocation()
						));
						continue;

					case CommonAnalyser.PropertyValidationResult.NotSimpleLambdaExpression:
					case CommonAnalyser.PropertyValidationResult.LambdaDoesNotTargetProperty:
						context.ReportDiagnostic(Diagnostic.Create(
							SimplePropertyAccessorArgumentAccessRule,
							argumentValue.GetLocation()
						));
						continue;

					case CommonAnalyser.PropertyValidationResult.MissingGetter:
						context.ReportDiagnostic(Diagnostic.Create(
							SimplePropertyAccessorArgumentAccessRule,
							argumentValue.GetLocation()
						));
						continue;

					case CommonAnalyser.PropertyValidationResult.GetterHasBridgeAttributes:
					case CommonAnalyser.PropertyValidationResult.SetterHasBridgeAttributes:
						context.ReportDiagnostic(Diagnostic.Create(
							BridgeAttributeAccessRule,
							argumentValue.GetLocation()
						));
						continue;

					case CommonAnalyser.PropertyValidationResult.PropertyIsOfMoreSpecificTypeThanSpecificValueType:
						context.ReportDiagnostic(Diagnostic.Create(
							PropertyMayNotBeSetToInstanceOfLessSpecificTypeRule,
							invocation.GetLocation(),
							propertyIfSuccessfullyRetrieved.GetMethod.ReturnType, // This will always have a value if we got PropertyIsOfMoreSpecificTypeThanSpecificValueType back
							parameterTypeNamedSymbol.DelegateInvokeMethod.ReturnType.Name
						));
						continue;
				}
			}

			// While we're looking at method calls, ensure that we don't pass a [PropertyIdentifier] argument for the current method into another method as an out or ref argument because reassignment
			// of [PropertyIdentifier] arguments is not allowed (because it would be too difficult - impossible, actually, I think - to ensure that it doesn't come back in a form that would mess up
			// With calls in bad ways)
			foreach (var argumentDetails in invocationArgumentDetails)
			{
				var argumentValue = invocation.ArgumentList.Arguments[argumentDetails.Index];
				if (argumentValue.RefOrOutKeyword.Kind() == SyntaxKind.None)
					continue;
				
				var argumentValueAsParameter = context.SemanticModel.GetSymbolInfo(argumentValue.Expression).Symbol as IParameterSymbol;
				if ((argumentValueAsParameter == null) || !CommonAnalyser.HasPropertyIdentifierAttribute(argumentValueAsParameter))
					continue;

				context.ReportDiagnostic(Diagnostic.Create(
					NoReassignmentRule,
					argumentValue.GetLocation()
				));
			}
		}

		private void LookForPropertyIdentifierReassignment(SyntaxNodeAnalysisContext context)
		{
			var assignment = context.Node as AssignmentExpressionSyntax;
			if (assignment == null)
				return;

			var targetName = assignment.Left as IdentifierNameSyntax;
			if (targetName == null)
				return;

			var assignmentTargetAsParameter = context.SemanticModel.GetSymbolInfo(assignment.Left).Symbol as IParameterSymbol;
			if ((assignmentTargetAsParameter == null) || !CommonAnalyser.HasPropertyIdentifierAttribute(assignmentTargetAsParameter))
				return;

			context.ReportDiagnostic(Diagnostic.Create(
				NoReassignmentRule,
				assignment.Left.GetLocation()
			));
		}

		private static LocalizableString GetLocalizableString(string nameOfLocalizableResource)
		{
			return new LocalizableResourceString(nameOfLocalizableResource, Resources.ResourceManager, typeof(Resources));
		}
	}
}