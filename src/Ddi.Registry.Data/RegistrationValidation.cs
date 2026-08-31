namespace Ddi.Registry.Data
{
    public static class RegistrationValidation
    {
        public static bool IsVariablePublishable(
            ApprovalState variable,
            ApprovalState concept,
            ApprovalState representation)
        {
            return variable == ApprovalState.Approved
                && concept == ApprovalState.Approved
                && representation == ApprovalState.Approved;
        }

        public static RegistrationValidationResult ValidateVariableReferences(
            string variableAgencyId,
            string conceptAgencyId,
            string representationAgencyId,
            bool allowCrossAgency)
        {
            if (!allowCrossAgency
                && (variableAgencyId != conceptAgencyId || variableAgencyId != representationAgencyId))
            {
                return RegistrationValidationResult.Invalid("CrossAgencyReference", "Variable references must remain within the same agency.");
            }

            return RegistrationValidationResult.Valid();
        }
    }

    public sealed record RegistrationValidationResult(bool IsValid, string ErrorCode, string ErrorMessage)
    {
        public static RegistrationValidationResult Valid()
            => new(true, string.Empty, string.Empty);

        public static RegistrationValidationResult Invalid(string errorCode, string errorMessage)
            => new(false, errorCode, errorMessage);
    }
}