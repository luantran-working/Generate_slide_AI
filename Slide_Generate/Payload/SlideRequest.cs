namespace Slide_Generate.Payload
{
    public class SlideCodeDto
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public int Slidenum { get; set; }
        public string ImageId { get; set; }
        public List<SlidePointDto> Body { get; set; }
        public string Talktrack { get; set; }
        public List<SlideSourceDto> Sources { get; set; }
        public bool ForceEdit { get; set; }
    }
}   
