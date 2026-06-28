using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: CLSCompliant(true)]
[assembly: ComVisible(false)]

// Catalogue.Default / QualificationScale.Default are immutable shipped snapshots with no installer (FDG L2);
// InternalsVisibleTo remains for the source-generated JSON context and the test seam.
[assembly: InternalsVisibleTo("EnrolmentRules.Engine")]
[assembly: InternalsVisibleTo("EnrolmentRules.Tests")]
