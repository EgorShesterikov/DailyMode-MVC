using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniIT.Framework;

namespace App
{
	public class LobbyDailyController : IDisposable
	{
		private const string TODAY_TEXT_KEY = "DATE_CURRENT";
		private const string BADGE_MONTH_TEXT_KEY = "BADGE_MONTH{0}";
		private const string DATE_MONTH_TEXT_KEY = "DATE_MONTH{0}";
		private const string DATE_MONTH_SHORT_TEXT_KEY = "DATE_MONTH{0}_SHORT";

		private const int MAX_DAYS_IN_MONTH = 31;

		private readonly LobbyDailyService _lobbyDailyService;
		private readonly IAdService _adService;
		private readonly AppConfig _config;

		public event Action OnJourneyModeReturned = delegate { };

		private LobbyDailyView _view;
		
		private Stopwatch _stopwatch;
		private CancellationTokenSource _stopwatchCancellationTokenSource;
		private CancellationTokenSource _cancellationTokenSource;

		public LobbyDailyController(LobbyDailyService lobbyDailyService, IAdService adService, AppConfig appConfig)
		{
			_lobbyDailyService = lobbyDailyService;
			_adService = adService;
			_config = appConfig;
		}

		public void Init(LobbyDailyView view)
		{
			_view = view;

			_view.AddArrowLeftButtonOnCickHandler(OnArrowLeftButtonPressed);
			_view.AddArrowRightButtonOnCickHandler(OnArrowRightButtonPressed);
			_view.AddPlayButtonDailyOnCickHandler(OnPlayButtonPressed);
			_view.AddPlayAdsButtonDailyOnCickHandler(OnPlayAdsButtonPressed);

			_view.OnChangeCurrentDay += ChangeCurrentDay;

			_cancellationTokenSource = new CancellationTokenSource();
		}

		public void ShowDailyLobby()
		{
			_lobbyDailyService.CalculateCurrentDateTime();
			RefreshDailyLobby(true);
		}

		private void RefreshDailyLobby(bool instantAction = false)
		{
			_stopwatchCancellationTokenSource?.Cancel();
			_stopwatchCancellationTokenSource = new CancellationTokenSource();

			_view.ResetParams();

			DateTime currentDateTime = _lobbyDailyService.GetCurrentDate();
			DateTime nowDateTime = _lobbyDailyService.GetDateNow();

			int daysInMonth = DateTime.DaysInMonth(currentDateTime.Year, currentDateTime.Month);
			int viewDays = currentDateTime.CompareYearAndMonthTo(nowDateTime) ? nowDateTime.Day : daysInMonth;

			const int CONSTANTLY_ACTIVE_OBJECTS_IN_SCROLL_RECT = 4;
			int activeObjectsInContentCount = MAX_DAYS_IN_MONTH + CONSTANTLY_ACTIVE_OBJECTS_IN_SCROLL_RECT - (MAX_DAYS_IN_MONTH - viewDays);

			SetCurrentDate(currentDateTime);
			SetCalendarItemAsPreviousMonth(currentDateTime);
			SetDaysInMonth(viewDays, currentDateTime, nowDateTime);
			SetCalendarItemAsNextMonth(viewDays, daysInMonth, nowDateTime, currentDateTime);

			_view.SetActiveObjectsInContentCount(activeObjectsInContentCount);

			int lastAvailableDayInCurrentMonth = _lobbyDailyService.GetLastAvailableDayInCurrentMonth();
			ShowCompletedDayAndProgress(lastAvailableDayInCurrentMonth == 0 ? viewDays : lastAvailableDayInCurrentMonth, instantAction);

			OpenPreviousMonthIfPossible();
			OpenNextMonthIfPossible();
		}

		private void SetCurrentDate(DateTime currentDateTime)
		{
			string longMonth = GetMonthLongName(currentDateTime.Month);

			_view.SetCurrentDate($"{longMonth} {currentDateTime: `yy}");
		}

		private void SetCalendarItemAsPreviousMonth(DateTime currentDateTime)
		{
			_view.SetCalendarItemAsPreviousMonth(GetMonthLongName(currentDateTime.AddMonths(-1).Month));
		}

		private void SetDaysInMonth(int viewDays, DateTime currentDateTime, DateTime nowDateTime)
		{
			string shortMonth = GetMonthShortName(currentDateTime.Month);

			for (int day = 1; day <= MAX_DAYS_IN_MONTH; day++)
			{
				if (day <= viewDays)
				{
					LevelState levelState = LevelState.Locked;

					if (_lobbyDailyService.GetDailyLevelState(day) == CalendarItemView.STATE_COMPLETED)
					{
						levelState = LevelState.Complete;
					}

					if (currentDateTime.CompareYearAndMonthTo(nowDateTime) && nowDateTime.Day == day)
					{
						_view.SetCalendarItemAsLevel(day, LocaleManager.GetString(string.Format(TODAY_TEXT_KEY)), levelState);
					}
					else
					{
						_view.SetCalendarItemAsLevel(day, string.Format(shortMonth, day), levelState);
					}
				}
				else
				{
					_view.HideCalendarItem(day);
				}
			}
		}

		private void SetCalendarItemAsNextMonth(int viewDays, int daysInMonth, DateTime nowDateTime, DateTime currentDateTime)
		{
			if (viewDays <= daysInMonth && currentDateTime.CompareYearAndMonthTo(nowDateTime))
			{
				TimeSpan difference = nowDateTime.Date.AddDays(1) - nowDateTime;
				StartNextDayTimerAsync(difference).Forget();
			}
			else
			{
				_view.SetCalendarItemAsNextMonth(string.Format(GetMonthLongName(currentDateTime.AddMonths(1).Month), viewDays + 1));
			}
		}

		private void ShowCompletedDayAndProgress(int viewDays, bool instantAction)
		{
			if (_lobbyDailyService.TryGetAndResetPlayedDailyDateDay(out int day))
			{
				_view.ScrollCalendarToTarget(day, instantAction);

				bool dayIsCompleted = _lobbyDailyService.GetDailyLevelState(day) == CalendarItemView.STATE_COMPLETED;

				if (dayIsCompleted)
				{
					int lastAvailableDay = _lobbyDailyService.GetLastAvailableDayInCurrentMonth();

					if (lastAvailableDay > 0)
					{
						const int DELAYED_SCROLL_CALENDAR_TO_TARGET = 1;
						_view.DelayedScrollCalendarToTargetAsync(DELAYED_SCROLL_CALENDAR_TO_TARGET, lastAvailableDay).Forget();
					}

					_view.SetCompletedDayAnim(day, () => _view.SetProgressOfMonth(_lobbyDailyService.GetCurrentDateProgress(), false, true));
				}

				_view.SetProgressOfMonth(_lobbyDailyService.GetCurrentDateProgress(dayIsCompleted), true);
			}
			else
			{
				_view.ScrollCalendarToTarget(viewDays, instantAction);
				_view.SetProgressOfMonth(_lobbyDailyService.GetCurrentDateProgress(), true);
			}
		}

		private async UniTaskVoid StartNextDayTimerAsync(TimeSpan targetTime)
		{
			_stopwatch = Stopwatch.StartNew();

			while (_stopwatch.Elapsed.TotalSeconds < targetTime.TotalSeconds)
			{
				TimeSpan difference = targetTime - _stopwatch.Elapsed;
				_view.SetCalendarItemAsWaitForDate(difference.GetTextTimeFormat(true));

				int cacheSecond = _stopwatch.Elapsed.Seconds;

				await UniTask.WaitUntil(() => cacheSecond != _stopwatch.Elapsed.Seconds, cancellationToken: _stopwatchCancellationTokenSource.Token);
			}

			RefreshDailyLobby();
		}

		private void OpenPreviousMonthIfPossible()
		{
			DateTime currentDateTime = _lobbyDailyService.GetCurrentDate();
			DateTime firstEnterDateTime = _lobbyDailyService.GetFirstEnterDate();

			_view.SetActiveLeftArrowButton(currentDateTime.IsDateAfterByYearMonth(firstEnterDateTime) || _lobbyDailyService.IsAllDaysCompletedInCurrentDate());
		}

		private void OpenNextMonthIfPossible()
		{
			DateTime currentDateTime = _lobbyDailyService.GetCurrentDate();
			DateTime nowDateTime = _lobbyDailyService.GetDateNow();

			_view.SetActiveRightArrowButton(currentDateTime.IsDateBeforeByYearMonth(nowDateTime));
		}

		private void ChangeCurrentDay(int day)
		{
			_lobbyDailyService.SetDayInCurrentDate(day);

			DateTime nowDateTime = _lobbyDailyService.GetDateNow();
			DateTime currentDateTime = _lobbyDailyService.GetCurrentDate();

			bool isToday = nowDateTime.Date == currentDateTime.Date && nowDateTime.Day == day;
			bool isDailyEarlyLevelsRewardedEnabled = _config.GetDailyEarlyLevelsRewardedEnabled();
			bool isDailyLevelStateActive = _lobbyDailyService.GetDailyLevelState(day) == CalendarItemView.STATE_ACTIVE;

			if (isToday)
			{
				_view.SetCurrentDateInInfoPanel(LocaleManager.GetString(string.Format(TODAY_TEXT_KEY)));
			}
			else
			{
				_view.SetCurrentDateInInfoPanel(string.Format(LocaleManager.GetString(string.Format(DATE_MONTH_TEXT_KEY, currentDateTime.Month)), day));
			}

			if (!isDailyEarlyLevelsRewardedEnabled)
			{
				_view.DisplayPlayButton();
				return;
			}

			if (isToday && isDailyLevelStateActive)
			{
				_view.DisplayPlayButton();
			}
			else
			{
				_view.DisplayAdsPlayButton();
			}
		}

		private string GetMonthLongName(int month)
		{
			return LocaleManager.GetString(string.Format(BADGE_MONTH_TEXT_KEY, month));
		}

		private string GetMonthShortName(int month)
		{
			return LocaleManager.GetString(string.Format(DATE_MONTH_SHORT_TEXT_KEY, month));
		}

		private void OnArrowLeftButtonPressed()
		{
			_lobbyDailyService.ReduceMonthInCurrentDate();
			RefreshDailyLobby();
		}

		private void OnArrowRightButtonPressed()
		{
			_lobbyDailyService.IncMonthInCurrentDate();
			RefreshDailyLobby();
		}

		private void OnPlayButtonPressed()
		{
			TryStartLevel(DefaultStartLevel);
		}

		private void OnPlayAdsButtonPressed()
		{
			TryStartLevel(ShowAdsAndThenStartLevel);
		}

		private void TryStartLevel(Action startMethod)
		{
			int lastAvailableDay = _lobbyDailyService.GetLastAvailableDayInCurrentMonth();

			if (lastAvailableDay != 0)
			{
				if (_lobbyDailyService.GetDailyLevelState(_lobbyDailyService.GetCurrentDate().Day) == CalendarItemView.STATE_ACTIVE)
				{
					startMethod.Invoke();
				}
				else
				{
					_view.ScrollCalendarToTarget(lastAvailableDay, false, startMethod.Invoke);
				}
			}
			else
			{
				DateTime cacheDateTime = _lobbyDailyService.GetCurrentDate();

				_lobbyDailyService.CalculateCurrentDateTime();

				if (cacheDateTime.CompareYearAndMonthTo(_lobbyDailyService.GetCurrentDate()))
				{
					OnJourneyModeReturned.Invoke();
				}
				else
				{
					RefreshDailyLobby();
					_view.ScrollCalendarToTarget(_lobbyDailyService.GetLastAvailableDayInCurrentMonth(), false, startMethod.Invoke);
				}
			}
		}

		private void DefaultStartLevel()
		{
			_lobbyDailyService.CreateOrTakeExistDailyLevel();
		}

		private void ShowAdsAndThenStartLevel()
		{
			_view.SetEnabledScrollRect(false);
			_view.SetInteractablePlayAdsButton(false);

			_adService.Rewarded.Show(null, "daily", (success, _) =>
			{
				_view.SetEnabledScrollRect(true);
				_view.SetInteractablePlayAdsButton(true);

				if (success)
				{
					_lobbyDailyService.CreateOrTakeExistDailyLevel();
				}
			});
		}

		public void Dispose()
		{
			_view.OnChangeCurrentDay -= ChangeCurrentDay;

			if (_stopwatchCancellationTokenSource != null)
			{
				_stopwatchCancellationTokenSource.Cancel();
				_stopwatchCancellationTokenSource.Dispose();
			}

			if (_cancellationTokenSource != null)
			{
				_cancellationTokenSource.Cancel();
				_cancellationTokenSource.Dispose();
			}
		}
	}
}
