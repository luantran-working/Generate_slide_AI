using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text;
using System.Net.Http.Headers;
using Domain.Payload.Request;
using Domain.Shares;
using System.Net.Http;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Domain.Payload.Base;
using System.Net;
using System.Diagnostics.CodeAnalysis;
using Discord.Net;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Authorization;
using Polly;
using JsonSerializer = System.Text.Json.JsonSerializer;
namespace AI_API.Controllers
{
    [ApiController]
    [Route("api")]
    public class AIController(ILogger<AIController> _logger, IConfiguration _configuration, IHttpClientFactory _httpClientFactory) : Controller
    {
        [HttpPost("text-to-speech")]
        public async Task<IActionResult> TextToSpeech([FromBody] PromtAI request)
        {
            if (!Request.Headers.TryGetValue("headerKey", out var headerKey) || headerKey != _configuration["IIT:Key"])
            {
                return Unauthorized(new
                {
                    Message = "Unauthorized: Invalid or missing headerKey"
                });
            }

            string url = $"{_configuration["MinimaxAI:BaseUrl"]}?GroupId={_configuration["MinimaxAI:GroupId"]}";

            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _configuration["MinimaxAI:ApiKey"]);
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var body = new
            {
                model = "speech-02-turbo",
                text = request.Text,
                stream = true,
                voice_setting = new
                {
                    voice_id = _configuration["MinimaxAI:VoiceId"],
                    speed = 1.0,
                    vol = 1.0,
                    pitch = 0,
                },
                audio_setting = new
                {
                    sample_rate = 32000,
                    bitrate = 128000,
                    format = "mp3",
                    channel = 1
                }
            };

            var jsonBody = System.Text.Json.JsonSerializer.Serialize(body);
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                HttpContext.RequestAborted
            );

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            using var memoryStream = new MemoryStream();

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line) && line.StartsWith("data:"))
                {
                    var jsonLine = line.Substring(5);
                    _logger.LogError("Received data: {JsonLine}", jsonLine);
                    using var doc = JsonDocument.Parse(jsonLine);

                    if (doc.RootElement.TryGetProperty("data", out var dataElement) &&
                        dataElement.TryGetProperty("audio", out var audioElement))
                    {
                        var audioHex = audioElement.GetString();
                        if (!string.IsNullOrEmpty(audioHex))
                        {
                            var audioBytes = Util.ConvertHexStringToByteArray(audioHex);
                            memoryStream.Write(audioBytes, 0, audioBytes.Length);
                        }
                    }
                }
            }

            memoryStream.Seek(0, SeekOrigin.Begin);
            return File(memoryStream.ToArray(), "audio/mpeg", "speech_output.mp3");
        }


        [HttpPost("gpt-text-to-speech")]
        public async Task<IActionResult> OpenAITextToSpeech([FromBody] PromtAI request)
        {
            if (!Request.Headers.TryGetValue("headerKey", out var headerKey) || headerKey != _configuration["IIT:Key"])
            {
                return Unauthorized(new
                {
                    Message = "Unauthorized: Invalid or missing headerKey"
                });
            }

            try
            {
                string url = $"{_configuration["OpenAI:ApiUrl"]}";
                string key = $"{_configuration["OpenAI:ApiKey"]}";
                using var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var body = new
                {
                    model = "tts-1",
                    input = request.Text,
                    voice = "nova",
                    response_format = "mp3"
                };

                var json = JsonSerializer.Serialize(body);
                var response = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return StatusCode((int)response.StatusCode, error);
                }

                var audioBytes = await response.Content.ReadAsByteArrayAsync();

                return File(audioBytes, "audio/mpeg", "openai_speech.mp3");
            }
            catch (Exception ex)
            {
                _logger.LogError("[OpenAI Text To Speech API]" + ex.Message, ex.StackTrace);
                return StatusCode(500, ex.ToString());
            }
        }

        [HttpPost("text-to-speech-man")]
        public async Task<IActionResult> EleventLabsTextToSpeechMan([FromBody] PromtAI request)
        {
            if (!Request.Headers.TryGetValue("headerKey", out var headerKey) || headerKey != _configuration["IIT:Key"])
            {
                return Unauthorized(new
                {
                    Message = "Unauthorized: Invalid or missing headerKey"
                });
            }

            string key = _configuration["Eleventlab:ApiKey"]!;
            string url = $"{_configuration["Eleventlab:BaseUrl"]}/v1/text-to-speech/{_configuration["Eleventlab:ManVoiceId"]}?output_format=mp3_44100_128";

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("xi-api-key", key);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            string cleanedText = Util.NormalizeVietnameseText(request.Text);

            var requestBody = new
            {
                text = cleanedText,
                model_id = "eleven_turbo_v2_5",
                voice_settings = new
                {
                    stability = 0.5,
                    similarity_boost = 0.98,
                    speed = 1.0,
                }
            };

            var jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync(url, content);
                var contentType = response.Content.Headers.ContentType?.MediaType;
                Console.WriteLine("Returned content-type: " + contentType);

                if (response.IsSuccessStatusCode)
                {
                    var audioBytes = await response.Content.ReadAsByteArrayAsync();
                    Console.WriteLine("Audio byte length: " + audioBytes.Length);

                    if (audioBytes.Length < 3000)
                    {
                        var debugText = await response.Content.ReadAsStringAsync();
                        Console.WriteLine("Cảnh báo: âm thanh có thể không hợp lệ.");
                        Console.WriteLine("Debug content: " + debugText);
                    }

                    return File(audioBytes, "audio/mpeg", "output.mp3");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return StatusCode((int)response.StatusCode, error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[Eleven Labs Text To Speech API] " + ex.Message, ex);
                return StatusCode(500, ex.ToString());
            }



        }

        [HttpPost("text-to-speech-woman")]
        public async Task<IActionResult> EleventLabsTextToSpeechWoman([FromBody] PromtAI request)
        {
            if (!Request.Headers.TryGetValue("headerKey", out var headerKey) || headerKey != _configuration["IIT:Key"])
            {
                return Unauthorized(new
                {
                    Message = "Unauthorized: Invalid or missing headerKey"
                });
            }

            string key = _configuration["Eleventlab:ApiKey"]!;
            string url = $"{_configuration["Eleventlab:BaseUrl"]}/v1/text-to-speech/{_configuration["Eleventlab:WomanVoiceId"]}?output_format=mp3_44100_128";

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("xi-api-key", key);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            string cleanedText = Util.NormalizeVietnameseText(request.Text);

            var requestBody = new
            {
                text = cleanedText,
                model_id = "eleven_turbo_v2_5",
                voice_settings = new
                {
                    stability = 0.5,
                    similarity_boost = 0.98,
                    speed = 1.0,
                }
            };

            var jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync(url, content);
                var contentType = response.Content.Headers.ContentType?.MediaType;
                Console.WriteLine("Returned content-type: " + contentType);

                if (response.IsSuccessStatusCode)
                {
                    var audioBytes = await response.Content.ReadAsByteArrayAsync();
                    Console.WriteLine("Audio byte length: " + audioBytes.Length);

                    if (audioBytes.Length < 3000)
                    {
                        var debugText = await response.Content.ReadAsStringAsync();
                        Console.WriteLine("Cảnh báo: âm thanh có thể không hợp lệ.");
                        Console.WriteLine("Debug content: " + debugText);
                    }

                    return File(audioBytes, "audio/mpeg", "output.mp3");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return StatusCode((int)response.StatusCode, error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[Eleven Labs Text To Speech API] " + ex.Message, ex);
                return StatusCode(500, ex.ToString());
            }
        }

        [HttpPost("speech-to-text")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> EventLabsSpeechToText([FromForm] AudioUploadRequest request)
        {
            if (!Request.Headers.TryGetValue("headerKey", out var headerKey) || headerKey != _configuration["IIT:Key"])
            {
                return Unauthorized(new
                {
                    Message = "Unauthorized: Invalid or missing headerKey"
                });
            }

            if (request.audioFile == null || request.audioFile.Length == 0)
            {
                return BadRequest(new { Message = "File không hợp lệ hoặc bị trống!" });
            }

            var allowedExtensions = new[] { ".mp3", ".wav", ".m4a" };
            var fileExtension = Path.GetExtension(request.audioFile.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(fileExtension))
            {
                return BadRequest(new { Message = $"Định dạng file không hợp lệ. Chỉ hỗ trợ: {string.Join(", ", allowedExtensions)}" });
            }

            try
            {
                using var stream = new MemoryStream();
                await request.audioFile.CopyToAsync(stream);
                stream.Position = 0;

                var apiKey = _configuration["Eleventlab:ApiKey"];
                var url = $"{_configuration["Eleventlab:BaseUrl"]}/v1/speech-to-text";

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("xi-api-key", apiKey);

                using var content = new MultipartFormDataContent();

                content.Add(new StringContent("scribe_v1"), "model_id");

                var fileContent = new StreamContent(stream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(request.audioFile.ContentType ?? "audio/mpeg");

                content.Add(fileContent, "file", request.audioFile.FileName);

                var response = await client.PostAsync(url, content);
                var resultContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var json = JsonDocument.Parse(resultContent).RootElement;
                    return Ok(json);
                }

                return StatusCode((int)response.StatusCode, resultContent);
            }
            catch (Exception ex)
            {
                _logger.LogError("[Speech to Text API] " + ex.Message, ex);
                return StatusCode(500, ex.ToString());
            }
        }

        [HttpPost("gpt-3.5-turbo")]
        public async Task<IActionResult> ChatAI([FromBody] PromtAI promt)
        {
            if (!Request.Headers.TryGetValue("headerKey", out var headerKey) || headerKey != _configuration["IIT:Key"])
            {
                return Unauthorized(new
                {
                    Message = "Unauthorized: Invalid or missing headerKey"
                });
            }

            string key = _configuration["OpenAI:ChatApiKey"]!;
            string url = $"{_configuration["OpenAI:ChatApiUrl"]}";
            string AiModel = _configuration["OpenAI:ChatModel"]!;

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

            var requestData = new
            {
                model = "gpt-3.5-turbo",
                prompt = promt.Text ?? "Hello Chat",
                max_tokens = 60,
                temperature = 0.7
            };

            string json = JsonSerializer.Serialize(requestData);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                return Ok(new { Message = "Success", Data = responseContent });
            }

            else
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error from OpenAI API: {ErrorContent}", errorContent);
                return StatusCode((int)response.StatusCode, new { Message = "Error", Error = errorContent });
            }

        }
     
        [HttpPost("chat-iit")]
        public async Task<IActionResult> GeniniChatBot([FromBody] PromtAI request)
        {
            try
            {
                if (!Request.Headers.TryGetValue("headerKey", out var headerKey) || headerKey != _configuration["IIT:Key"])
                {
                    return Content(@"
                <p><strong>⛔ Unauthorized</strong></p>
                <p>Invalid or missing <strong>headerKey</strong>.</p>
            ", "text/html");
                }

                string key = _configuration["Gemini:ApiKey"]!;
                string url = $"{_configuration["Gemini:ApiUrl"]}?key={key}";

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var body = new
                {
                    contents = new[]
                    {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new
                        {
                            text = "Bạn hãy đóng vai trò là AI Bảo Bảo của công ty IIT, công ty chuyên về lĩnh vực công nghệ phần mềm, hãy trả lời vấn đề sau: " + request.Text
                        }
                    }
                }
            }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(body);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);
                var resultJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(resultJson);
                    var text = doc.RootElement
                                  .GetProperty("candidates")[0]
                                  .GetProperty("content")
                                  .GetProperty("parts")[0]
                                  .GetProperty("text")
                                  .GetString();

                    string sanitizedText = text?
                        .Replace("**", "") 
                        .Replace("*", "")   
                        .Replace("```", "")
                        .Replace("`", "");  

                    string html = $@"
                <p>{sanitizedText}</p>
            ";

                    return Content(html, "text/html", Encoding.UTF8);
                }
                else
                {
                    _logger.LogError("IIT AI error: {Error}", resultJson);
                    string errorHtml = $@"
                <p><strong>⚠️ IIT AI Error</strong></p>
                <p><em>Chi tiết:</em></p>
                <p><del>{resultJson}</del></p>
            ";
                    return Content(errorHtml, "text/html", Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in IIT AI");
                string exceptionHtml = $@"
            <p><strong>❌ Internal Server Error</strong></p>
            <p><em>{ex.Message}</em></p>
        ";
                return Content(exceptionHtml, "text/html", Encoding.UTF8);
            }
        }




        [HttpPost("generate-slide")]
        [AllowAnonymous]
        public async Task<IActionResult> GenerateSlide([FromBody]PromtAI promt)
        {
            if (!Request.Headers.TryGetValue("headerKey", out var headerKey) || !headerKey.Equals(_configuration["IIT:Key"])){

                return Unauthorized(new
                {
                    Message = "Unauthorized: Invalid or missing headerKey"
                });
            }

            if (promt == null)
            {
                _logger.LogWarning("Invalid request: Request body is null");
                return BadRequest(JsonConvert.SerializeObject(new { error = "Yêu cầu không hợp lệ: Body rỗng" }));
            }

            string requestBodyJson = JsonConvert.SerializeObject(promt);
            _logger.LogDebug($"Received request body: {requestBodyJson}");

            if (string.IsNullOrWhiteSpace(promt.Text))
            {
                _logger.LogWarning("Invalid request: Prompt is empty or null");
                return BadRequest(JsonConvert.SerializeObject(new { error = "Prompt là bắt buộc và không được để trống" }));
            }

            try
            {
                _logger.LogInformation($"Processing prompt: {promt.Text}");

                var requestBody = new
                {
                    prompt = promt.Text.Trim(),
                    theme = "corporate",
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

                var client = _httpClientFactory.CreateClient("SlideGPTClient");
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

        [HttpPost("chat")]
        public async Task<IActionResult> GeniniChatBotMarkdown([FromBody] PromtAI request)
        {
            try
            {
                if (!Request.Headers.TryGetValue("headerKey", out var headerKey) || headerKey != _configuration["IIT:Key"])
                {
                    return Content("⛔ **Unauthorized**\n\nInvalid or missing `headerKey`.", "text/markdown");
                }

                string apiKey = _configuration["Gemini:ApiKey"]!;
                string apiUrl = $"{_configuration["Gemini:ApiUrl"]}?key={apiKey}";

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var requestBody = new
                {
                    contents = new[]
                    {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new
                        {
                            text = "Bạn hãy đóng vai trò là AI Bảo Bảo của công ty IIT, công ty chuyên về lĩnh vực công nghệ phần mềm, hãy trả lời vấn đề sau: " + request.Text
                        }
                    }
                }
            }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(apiUrl, content);
                var resultJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(resultJson);

                    if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                        candidates.GetArrayLength() > 0 &&
                        candidates[0].TryGetProperty("content", out var contentElement) &&
                        contentElement.TryGetProperty("parts", out var partsElement) &&
                        partsElement.GetArrayLength() > 0 &&
                        partsElement[0].TryGetProperty("text", out var textElement))
                    {
                        var text = textElement.GetString();

                        return Ok(new { data = text });
                    }

                    return Ok(new { data = new { text = "⚠ Không tìm thấy nội dung từ AI." } });
                }
                else
                {
                    return Content($"{{\"text\": \"❌ Error {response.StatusCode}: {resultJson}\"}}", "application/json", Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Lỗi xử lý trong AI Controller");
                return Content($"{{\"text\": \"❌ Internal Server Error: {ex.Message}\"}}", "application/json", Encoding.UTF8);
            }
        }
    }
}
