using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Workit.Shared.Api;
using Workit.Shared.Models;

namespace Workit.Tests.Api;

public class JobAttachmentsApiTests
{
    private sealed class FakeToken : IAccessTokenAccessor
    {
        public ValueTask<string?> GetAccessTokenAsync() => ValueTask.FromResult<string?>("jwt");
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return factory(request);
        }
    }

    private static (JobAttachmentsApi api, RecordingHandler handler) Create(Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        var handler = new RecordingHandler(factory);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        return (new JobAttachmentsApi(http, new FakeToken()), handler);
    }

    [Fact]
    public async Task GetForJob_CallsJobScopedUrl_WithBearer()
    {
        var jobId = Guid.NewGuid();
        var (api, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<JobAttachmentInfo>())
        });

        var result = await api.GetForJobAsync(jobId);

        result.IsSuccess.Should().BeTrue();
        handler.Last!.RequestUri!.PathAndQuery.Should().Be($"/api/jobs/{jobId}/attachments");
        handler.Last.Headers.Authorization!.Parameter.Should().Be("jwt");
    }

    [Fact]
    public async Task GetForJob_NullBody_ReturnsEmptyList()
    {
        var (api, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create<List<JobAttachmentInfo>?>(null)
        });

        var result = await api.GetForJobAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task Upload_SendsMultipartFilePart()
    {
        var jobId = Guid.NewGuid();
        var (api, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new JobAttachmentInfo { Id = Guid.NewGuid(), FileName = "p.png" })
        });

        var bytes = new MemoryStream([1, 2, 3]);
        var result = await api.UploadAsync(jobId, bytes, "p.png", "image/png");

        result.IsSuccess.Should().BeTrue();
        handler.Last!.Method.Should().Be(HttpMethod.Post);
        handler.Last.RequestUri!.PathAndQuery.Should().Be($"/api/jobs/{jobId}/attachments");
        handler.Last.Content!.Headers.ContentType!.MediaType.Should().Be("multipart/form-data");
        handler.LastBody.Should().Contain("name=file").And.Contain("p.png");
    }

    [Fact]
    public async Task Upload_Failure_SurfacesServerMessage()
    {
        var (api, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Only images and PDF files are allowed.")
        });

        var result = await api.UploadAsync(Guid.NewGuid(), new MemoryStream([1]), "x.exe", "application/x-msdownload");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Only images and PDF files are allowed.");
    }

    [Fact]
    public async Task Download_ReturnsBytes()
    {
        var payload = new byte[] { 5, 6, 7 };
        var (api, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });

        var jobId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var result = await api.DownloadAsync(jobId, id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(payload);
        handler.Last!.RequestUri!.PathAndQuery.Should().Be($"/api/jobs/{jobId}/attachments/{id}");
    }

    [Fact]
    public async Task Delete_CallsDelete()
    {
        var (api, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var jobId = Guid.NewGuid();
        var id = Guid.NewGuid();

        var result = await api.DeleteAsync(jobId, id);

        result.IsSuccess.Should().BeTrue();
        handler.Last!.Method.Should().Be(HttpMethod.Delete);
        handler.Last.RequestUri!.PathAndQuery.Should().Be($"/api/jobs/{jobId}/attachments/{id}");
    }
}
