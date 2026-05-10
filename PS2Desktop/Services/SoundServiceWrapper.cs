using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.Services
{
    public class SoundServiceWrapper : ISoundService
    {
        public void PlayClick()
        {
            SoundService.PlayClick();
        }
    }
}
