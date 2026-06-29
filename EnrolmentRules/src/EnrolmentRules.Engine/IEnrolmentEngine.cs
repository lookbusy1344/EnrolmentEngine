namespace EnrolmentRules.Engine;

/// <summary>
///     The full evaluation surface: verdict, explanation, and counterfactual advice. Prefer
///     <see cref="IEnrolmentEvaluator" /> for request-path hosting and <see cref="IEnrolmentAdvisor" />
///     for diagnostic tooling.
/// </summary>
public interface IEnrolmentEngine : IEnrolmentEvaluator, IEnrolmentAdvisor;
