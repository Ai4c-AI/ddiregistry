namespace Ddi.Registry.Web
{
    /// <summary>
    /// Marker class that serves as the shared resource source for data annotations
    /// and shared UI strings (navigation, common validation messages).
    /// </summary>
    /// <remarks>
    /// This class deliberately lives at the project root (not under the
    /// <c>Resources</c> folder) with the root namespace <c>Ddi.Registry.Web</c>.
    /// With <c>LocalizationOptions.ResourcesPath = "Resources"</c>, the resource
    /// base name resolves to <c>Ddi.Registry.Web.Resources.SharedResource</c>,
    /// which matches the embedded resource produced by <c>Resources/SharedResource.resx</c>.
    /// </remarks>
    public sealed class SharedResource
    {
    }
}
