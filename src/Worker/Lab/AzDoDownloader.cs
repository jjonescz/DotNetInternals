using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.OAuth;
using Microsoft.VisualStudio.Services.WebApi;

namespace DotNetInternals.Lab;

internal sealed class AzDoDownloader
{
    public async Task DownloadAsync()
    {
        var baseUrl = new Uri("https://dev.azure.com/dnceng-public");
        var credentials = new VssOAuthAccessTokenCredential("eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsIng1dCI6Im9PdmN6NU1fN3AtSGpJS2xGWHo5M3VfVjBabyJ9.eyJjaWQiOiIzM2Q2YzAzOS0zZmI4LTQ0NDEtOGYzMC1mMTcyMTQ3NzJmNjYiLCJjc2kiOiJmODU1MjYwOC0zNGRkLTRlMDgtODIyMS1jYWMyYjE0ZDc3OTAiLCJuYW1laWQiOiI2NzhhYjM3MS1iMmI4LTQ5ODktYTEyMS1lMDQ5YzhhNDQzMWYiLCJpc3MiOiJhcHAudnN0b2tlbi52aXN1YWxzdHVkaW8uY29tIiwiYXVkIjoiYXBwLnZzdG9rZW4udmlzdWFsc3R1ZGlvLmNvbSIsIm5iZiI6MTcyOTUyMDE2OSwiZXhwIjoxODg3Mjg2NTY5fQ.NJhKSVmoDSV3PiNIIvdVKSk4ytwCNep1WN1oYq5kTapnRsz28AlPzuDmeifs_c0yaKv8WCRaGMNe46m_ObLrQWLEw9F94VGAJpzj-V3Z-iIyoAM4CcxbxhE8ArfGRYqn_gAcReF4vAPz2xwyu4U39FckENEYIfT5f1psvOLIvFRTQfsCJxZ4a1kO-de3L4SBJFvVBTGqe4tA9v-UZ_bDCnafEU4Os90Nj8Vao4ELpKqFVlBFlPCJ8RPiiwcV_Y52mS2d1oVodZthSMFtx6168w4o_ZudpidAB5UAxArmfY0EFxLA7d1cTOIQxGETn0d2MlhKZI6Z3dthJ3XvYJva6w");
        var connection = new VssConnection(baseUrl, credentials);
        var buildClient = connection.GetClient<BuildHttpClient>();
        var artifact = await buildClient.GetArtifactAsync(
            project: "public",
            buildId: 848478,
            artifactName: "PackageArtifacts");
    }
}
