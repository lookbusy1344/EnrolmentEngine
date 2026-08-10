namespace EnrolmentRules.Engine;

/// <summary>
///     The full evaluation surface: verdict, explanation, counterfactual advice, and the plain-English
///     criteria behind them. Prefer <see cref="IEnrolmentEvaluator" /> for request-path hosting,
///     <see cref="IEnrolmentAdvisor" /> for diagnostic tooling, and
///     <see cref="IEnrolmentCriteriaExplainer" /> for prospectus-style output.
/// </summary>
public interface IEnrolmentEngine : IEnrolmentEvaluator, IEnrolmentAdvisor, IEnrolmentCriteriaExplainer;
