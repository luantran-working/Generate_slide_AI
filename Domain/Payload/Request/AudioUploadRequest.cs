using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Domain.Payload.Request
{
    public class AudioUploadRequest
    {
        public IFormFile audioFile {  get; set; }
    }
}
