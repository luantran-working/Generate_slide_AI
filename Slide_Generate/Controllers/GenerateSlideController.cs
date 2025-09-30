using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Polly;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Slide_Generate.Controllers
{
    public class GenerateSlideController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GenerateSlideController> _logger;
        private readonly IHttpClientFactory _clientFactory;

        public GenerateSlideController(IConfiguration configuration, ILogger<GenerateSlideController> logger, IHttpClientFactory clientFactory)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult GenerateSlideView()
        {
            _logger.LogInformation("GenerateSlideView action called");
            return View("GenerateSlideView");
        }


        [HttpPost("generate-slide")]
        [AllowAnonymous]
        public async Task<IActionResult> GenerateSlide([FromBody] GenerateSlideRequest request)
        {
            if (request == null)
            {
                _logger.LogWarning("Invalid request: Request body is null");
                return BadRequest(JsonConvert.SerializeObject(new { error = "Yêu cầu không hợp lệ: Body rỗng" }));
            }

            string requestBodyJson = JsonConvert.SerializeObject(request);
            _logger.LogDebug($"Received request body: {requestBodyJson}");

            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                _logger.LogWarning("Invalid request: Prompt is empty or null");
                return BadRequest(JsonConvert.SerializeObject(new { error = "Prompt là bắt buộc và không được để trống" }));
            }

            try
            {
                _logger.LogInformation($"Processing prompt: {request.Prompt}");

                var requestBody = new
                {
                    prompt = request.Prompt.Trim(),
                    theme = "modern",
                    language = "vi",
                    stock_images = true,
                    tone = "professional"
                };

                string apiRequestJson = JsonConvert.SerializeObject(requestBody);
                _logger.LogDebug($"Sending API request body: {apiRequestJson}");

                var apiUrl = _configuration["SlideGPT:ApiUrl"] ?? "https://api.slidesgpt.com/v1/presentations/generate";
                var apiToken = _configuration["SlideGPT:ApiToken"];

                if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(apiToken))
                {
                    _logger.LogError("SlideGPT:ApiUrl hoặc SlideGPT:ApiToken chưa được cấu hình");
                    return StatusCode(500, JsonConvert.SerializeObject(new { error = "Thiếu cấu hình API" }));
                }

                var retryPolicy = Policy
                    .Handle<HttpRequestException>()
                    .Or<TaskCanceledException>()
                    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        (exception, timeSpan, retryCount, context) =>
                        {
                            _logger.LogWarning($"Thử lại lần {retryCount} sau {timeSpan.TotalSeconds}s do {exception.Message}");
                        });

                var client = _clientFactory.CreateClient("SlideGPTClient");
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiToken}");
                _logger.LogInformation($"Gọi API: {apiUrl}");

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await retryPolicy.ExecuteAsync(async () =>
                {
                    return await client.PostAsync(apiUrl, content);
                });

                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"API Response Status: {response.StatusCode}");
                _logger.LogDebug($"API Response Content: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    var responseData = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    var apiBaseUrl = new Uri(apiUrl).GetLeftPart(UriPartial.Authority); // https://api.slidesgpt.com

                    string presentationId = responseData?.id?.ToString();
                    if (string.IsNullOrEmpty(presentationId))
                    {
                        _logger.LogError("API response thiếu presentation ID");
                        return StatusCode(500, JsonConvert.SerializeObject(new { error = "Phản hồi API không chứa ID của presentation" }));
                    }

                    string embedUrl = responseData?.embed?.ToString();
                    string downloadUrl = responseData?.download?.ToString();

                    if (!embedUrl.StartsWith("http"))
                        embedUrl = $"https://{embedUrl}";
                    if (!downloadUrl.StartsWith("http"))
                        downloadUrl = $"https://{downloadUrl}";

                    _logger.LogInformation($"Generated embed URL: {embedUrl}");
                    _logger.LogInformation($"Generated download URL: {downloadUrl}");

                    var result = new
                    {
                        embed = embedUrl,
                        download = downloadUrl,
                        presentationId,
                        apiToken
                    };

                    return Content(JsonConvert.SerializeObject(result), "application/json");
                }
                else
                {
                    _logger.LogError($"API Error: {response.StatusCode} - {responseContent}");
                    return StatusCode((int)response.StatusCode, JsonConvert.SerializeObject(new { error = $"Lỗi API: {responseContent}" }));
                }
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError($"[Generate Slide API] Timeout: {ex.Message}\n{ex.StackTrace}");
                return StatusCode(504, JsonConvert.SerializeObject(new { error = "Yêu cầu API bị timeout sau 100 giây. Thử lại hoặc liên hệ hỗ trợ API." }));
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Generate Slide API] Lỗi bất ngờ: {ex.Message}\n{ex.StackTrace}");
                return StatusCode(500, JsonConvert.SerializeObject(new { error = "Đã xảy ra lỗi bất ngờ. Vui lòng thử lại sau." }));
            }
        }

        public class GenerateSlideRequest
        {
            [JsonProperty("prompt")]
            public string Prompt { get; set; }
        }
    }
}
