using PS2Desktop.Services.Interfaces;

namespace PS2Desktop.ViewModels
{
    public class HomeViewModel : BaseViewModel
    {
        private readonly IThemeRepository _themeRepo;
        private readonly IGameRepository _gameRepo;
        private readonly IUserRepository _userRepo;
        private readonly IVoteRepository _voteRepo;

        private int _themeCount;
        private int _gameCount;
        private int _userCount;
        private double _globalRating;
        private int _globalVotes;
        private bool _isLoading;

        public HomeViewModel(
            IThemeRepository themeRepo,
            IGameRepository gameRepo,
            IUserRepository userRepo,
            IVoteRepository voteRepo)
        {
            _themeRepo = themeRepo;
            _gameRepo = gameRepo;
            _userRepo = userRepo;
            _voteRepo = voteRepo;
        }

        public int ThemeCount { get => _themeCount; set => SetProperty(ref _themeCount, value); }
        public int GameCount { get => _gameCount; set => SetProperty(ref _gameCount, value); }
        public int UserCount { get => _userCount; set => SetProperty(ref _userCount, value); }
        public double GlobalRating { get => _globalRating; set => SetProperty(ref _globalRating, value); }
        public int GlobalVotes { get => _globalVotes; set => SetProperty(ref _globalVotes, value); }
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

        public async Task LoadStatsAsync()
        {
            IsLoading = true;
            try
            {
                var taskTheme = _themeRepo.GetThemeCountAsync();
                var taskGame = _gameRepo.GetGameCountAsync();
                var taskUser = _userRepo.GetUserCountAsync();
                var taskRating = _voteRepo.GetGlobalAverageRatingAsync();

                await Task.WhenAll(taskTheme, taskGame, taskUser, taskRating);

                ThemeCount = await taskTheme;
                GameCount = await taskGame;
                UserCount = await taskUser;
                var (avg, cnt) = await taskRating;
                GlobalRating = Math.Round(avg, 1);
                GlobalVotes = cnt;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
