﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestHelper;

namespace ProductiveRage.Immutable.Analyser.Test
{
	[TestClass]
	public class PropertyIdentifierAttributeAnalyzerTests : DiagnosticVerifier
	{
		/// <summary>
		/// This is the simplest case - a method (Test2) takes a [PropertyIdentifier] argument that will identify a property of type int from a SomethingWithAnId reference and the
		/// provided lambda is a simple Id property access
		/// </summary>
		[TestMethod]
		public void IdealCaseForPropertyIdentifierArgument()
		{
			var testContent = @"
				using System;
				using ProductiveRage.Immutable;

				namespace TestCase
				{
					public class Example
					{
						public void Test()
						{
							Test2(_ => _.Id);
						}

						public void Test2([PropertyIdentifier] Func<SomethingWithAnId, int> propertyIdentifier) { }
					}

					public class SomethingWithAnId : IAmImmutable
					{
						public SomethingWithAnId(int id)
						{
							this.CtorSet(_ => _.Id, id);
						}
						public int Id { get; }
					}
				}";

			VerifyCSharpDiagnostic(testContent);
		}

		/// <summary>
		/// This is probably the simplest example of a malformed request - a method (Test2) takes a [PropertyIdentifier] argument that will identify a property of type int from a SomethingWithAnId
		/// reference but the provided lambda returns a constant instead of accessing a property
		/// </summary>
		[TestMethod]
		public void LambdaMustAccessProperty()
		{
			var testContent = @"
				using System;
				using ProductiveRage.Immutable;

				namespace TestCase
				{
					public class Example
					{
						public void Test()
						{
							Test2(_ => 123);
						}

						public void Test2([PropertyIdentifier] Func<SomethingWithAnId, int> propertyIdentifier) { }
					}

					public class SomethingWithAnId : IAmImmutable
					{
						public SomethingWithAnId(int id)
						{
							this.CtorSet(_ => _.Id, id);
						}
						public int Id { get; }
					}
				}";

			var expected = new DiagnosticResult
			{
				Id = PropertyIdentifierAttributeAnalyzer.DiagnosticId,
				Message = PropertyIdentifierAttributeAnalyzer.SimplePropertyAccessorArgumentAccessRule.MessageFormat.ToString(),
				Severity = DiagnosticSeverity.Error,
				Locations = new[]
				{
					new DiagnosticResultLocation("Test0.cs", 11, 14)
				}
			};

			VerifyCSharpDiagnostic(testContent, expected);
		}

		/// <summary>
		/// This ensures that the pre-existing way to pass way around property identifiers work
		/// </summary>
		[TestMethod]
		public void PropertyIdentifierInstanceMayBePassed()
		{
			var testContent = @"
				using System;
				using ProductiveRage.Immutable;

				namespace TestCase
				{
					public class Example
					{
						public void Test()
						{
							var x = new SomethingWithAnId(123);
							Test2(x.GetProperty(_ => _.Id));
						}

						public void Test2([PropertyIdentifier] Func<SomethingWithAnId, int> propertyIdentifier) { }
					}

					public class SomethingWithAnId : IAmImmutable
					{
						public SomethingWithAnId(int id)
						{
							this.CtorSet(_ => _.Id, id);
						}
						public int Id { get; }
					}
				}";

			VerifyCSharpDiagnostic(testContent);
		}

		[TestMethod]
		public void TPropertyValueMustBeAtLeastAsSpecificAsTheTargetPropertyType()
		{
			var testContent = @"
				using System;
				using ProductiveRage.Immutable;

				namespace TestCase
				{
					public class Example
					{
						public void Test()
						{
							Test2(_ => _.Id);
						}

						public void Test2([PropertyIdentifier] Func<SomethingWithAnId, object> propertyIdentifier) { }
					}

					public class SomethingWithAnId : IAmImmutable
					{
						public SomethingWithAnId(string id)
						{
							this.CtorSet(_ => _.Id, id);
						}
						public string Id { get; }
					}
				}";

			var expected = new DiagnosticResult
			{
				Id = PropertyIdentifierAttributeAnalyzer.DiagnosticId,
				Message = string.Format(
					PropertyIdentifierAttributeAnalyzer.PropertyMayNotBeSetToInstanceOfLessSpecificTypeRule.MessageFormat.ToString(),
					"string",
					"Object"
				),
				Severity = DiagnosticSeverity.Error,
				Locations = new[]
				{
					new DiagnosticResultLocation("Test0.cs", 11, 8)
				}
			};

			VerifyCSharpDiagnostic(testContent, expected);
		}

		/// <summary>
		/// The [PropertyIdentifier] analyser ensures that an acceptable lambda is provided to method arguments that have the [PropertyIdentifier] attribute on them but it seems too complicated
		/// to try to ensure that any reassignment of the reference meets the required criteria so the analyser does not allow reassignment
		/// </summary>
		[TestMethod]
		public void PropertyIdentifierArgumentsMayNotBeReassigned()
		{
			var testContent = @"
				using System;
				using ProductiveRage.Immutable;

				namespace TestCase
				{
					public class Example
					{
						public void Test2([PropertyIdentifier] Func<SomethingWithAnId, object> propertyIdentifier)
						{
							propertyIdentifier = null;
						}
					}

					public class SomethingWithAnId : IAmImmutable
					{
						public SomethingWithAnId(string id)
						{
							this.CtorSet(_ => _.Id, id);
						}
						public string Id { get; }
					}
				}";

			var expected = new DiagnosticResult
			{
				Id = PropertyIdentifierAttributeAnalyzer.DiagnosticId,
				Message = PropertyIdentifierAttributeAnalyzer.NoReassignmentRule.MessageFormat.ToString(),
				Severity = DiagnosticSeverity.Error,
				Locations = new[]
				{
					new DiagnosticResultLocation("Test0.cs", 11, 8)
				}
			};

			VerifyCSharpDiagnostic(testContent, expected);
		}

		/// <summary>
		/// This is similar in nature to PropertyIdentifierArgumentsMayNotBeReassigned but ensures that a [PropertyIdentifier]-decorated argument doesn't get changed indirectly by means of
		/// being passed as an output argument in a method call
		/// </summary>
		[TestMethod]
		public void PropertyIdentifierArgumentsMayNotBeReassignedByBeingPassedAsOutArgument()
		{
			var testContent = @"
				using System;
				using ProductiveRage.Immutable;

				namespace TestCase
				{
					public class Example
					{
						public void Test2([PropertyIdentifier] Func<SomethingWithAnId, object> propertyIdentifier)
						{
							Test3(out propertyIdentifier);
						}

						public void Test3(out Func<SomethingWithAnId, object> propertyIdentifier)
						{
							propertyIdentifier = null;
						}
					}

					public class SomethingWithAnId : IAmImmutable
					{
						public SomethingWithAnId(string id)
						{
							this.CtorSet(_ => _.Id, id);
						}
						public string Id { get; }
					}
				}";

			var expected = new DiagnosticResult
			{
				Id = PropertyIdentifierAttributeAnalyzer.DiagnosticId,
				Message = PropertyIdentifierAttributeAnalyzer.NoReassignmentRule.MessageFormat.ToString(),
				Severity = DiagnosticSeverity.Error,
				Locations = new[]
				{
					new DiagnosticResultLocation("Test0.cs", 11, 14)
				}
			};

			VerifyCSharpDiagnostic(testContent, expected);
		}

		/// <summary>
		/// Same deal as PropertyIdentifierArgumentsMayNotBeReassignedByBeingPassedAsOutArgument but checking for ref argument instead of out
		/// </summary>
		[TestMethod]
		public void PropertyIdentifierArgumentsMayNotBeReassignedByBeingPassedAsRefArgument()
		{
			var testContent = @"
				using System;
				using ProductiveRage.Immutable;

				namespace TestCase
				{
					public class Example
					{
						public void Test2([PropertyIdentifier] Func<SomethingWithAnId, object> propertyIdentifier)
						{
							Test3(ref propertyIdentifier);
						}

						public void Test3(ref Func<SomethingWithAnId, object> propertyIdentifier) { }
					}

					public class SomethingWithAnId : IAmImmutable
					{
						public SomethingWithAnId(string id)
						{
							this.CtorSet(_ => _.Id, id);
						}
						public string Id { get; }
					}
				}";

			var expected = new DiagnosticResult
			{
				Id = PropertyIdentifierAttributeAnalyzer.DiagnosticId,
				Message = PropertyIdentifierAttributeAnalyzer.NoReassignmentRule.MessageFormat.ToString(),
				Severity = DiagnosticSeverity.Error,
				Locations = new[]
				{
					new DiagnosticResultLocation("Test0.cs", 11, 14)
				}
			};

			VerifyCSharpDiagnostic(testContent, expected);
		}

		/// <summary>
		/// This is just to confirm that [PropertyIdentifier] arguments MAY be allowed to be passed other methods, so long as it's not as an out or ref argument
		/// </summary>
		[TestMethod]
		public void PropertyIdentifierArgumentsMayBePassedToOtherMethodsAsLongAsNotAsOutOrRefArgument()
		{
			var testContent = @"
				using System;
				using ProductiveRage.Immutable;

				namespace TestCase
				{
					public class Example
					{
						public void Test2([PropertyIdentifier] Func<SomethingWithAnId, object> propertyIdentifier)
						{
							Test3(propertyIdentifier);
						}

						public void Test3(Func<SomethingWithAnId, object> propertyIdentifier) { }
					}

					public class SomethingWithAnId : IAmImmutable
					{
						public SomethingWithAnId(string id)
						{
							this.CtorSet(_ => _.Id, id);
						}
						public string Id { get; }
					}
				}";

			VerifyCSharpDiagnostic(testContent);
		}

		protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
		{
			return new PropertyIdentifierAttributeAnalyzer();
		}
	}
}