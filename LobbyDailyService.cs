using System;
using Cysharp.Threading.Tasks;
using MiniIT.Snipe;
using MiniIT.Snipe.Api;
using MiniIT.Utils;
using UnityEngine;
using Random = UnityEngine.Random;

namespace App
{
	public class LobbyDailyService
	{
		private const int TICKS_IN_ONE_DAY = 86400;

		private readonly UserDataService _userDataService;
		private readonly AppStateController _stateController;

        private DateTime _currentDateTime;

		public LobbyDailyService(UserDataService userDataService, AppStateController stateController)
		{
			_userDataService = userDataService;
			_stateController = stateController;
		}

		public void CalculateCurrentDateTime()
		{
			int playerDateTicks = _userDataService.GetPlayedDailyDateTicks();

			if (playerDateTicks != 0)
			{
				_currentDateTime = TimestampUtil.UtcDateTimeFromTimestamp(playerDateTicks);
			}
			else
			{
				DateTime dateNow = GetDateNow();
				DateTime firstEnterDate = GetFirstEnterDate();

				_currentDateTime = dateNow;

				if (!dateNow.IsDateAfterByYearMonth(firstEnterDate) && !IsAllDaysCompletedInCurrentDate())
				{
					return;
				}

				while (GetLastAvailableDayInCurrentMonth() == 0)
				{
					ReduceMonthInCurrentDate();
				}
			}
		}

		public DateTime GetFirstEnterDate()
		{
			return TimestampUtil.UtcDateTimeFromTimestamp(_userDataService.GetRegTS());
		}

		public DateTime GetDateNow()
		{
			return DateTime.UtcNow;
		}

		public DateTime GetCurrentDate()
		{
			return _currentDateTime;
		}

		public void SetDayInCurrentDate(int day)
		{
			_currentDateTime = _currentDateTime.AddDays(day - _currentDateTime.Day);
		}

		public void IncMonthInCurrentDate()
		{
			_currentDateTime = _currentDateTime.AddMonths(1);
		}

		public void ReduceMonthInCurrentDate()
		{
			_currentDateTime = _currentDateTime.AddMonths(-1);
		}

		public bool TryGetAndResetPlayedDailyDateDay(out int day)
		{
			day = _userDataService.GetPlayedDailyDateDay();

			if (day == 0)
			{
				return false;
			}

			_userDataService.SetPlayedDailyDateTicks(0);

			return true;
		}

		public void CreateOrTakeExistDailyLevel()
		{
			if (_userDataService.TryGetLevelDailyTable(out SnipeTable<SnipeTableLevelsDailyItem> levelsDaily))
			{
				int levelID = 1;
				int currentDateTicks = (int)TimestampUtil.GetTimestamp(_currentDateTime.Date);

				if (TryGetCreatedDailyLevelItem(currentDateTicks, out DailyLevelItem dailyItem))
				{
					if (dailyItem.State == CalendarItemView.STATE_ACTIVE)
					{
						levelID = dailyItem.LevelID;
					}
					else
					{
						Debug.LogError("[LobbyDailyService] CreateOrGetDailyLevelId - The level has been passed, it cannot be started");
					}
				}
				else
				{
					if (_userDataService.DailyLevelItems.Count < levelsDaily.Count)
					{
						_userDataService.DailyLevelsCount.Value++;
						levelID = levelsDaily[_userDataService.DailyLevelsCount].preset;
					}
					else
					{
						levelID = levelsDaily[Random.Range(1, levelsDaily.Count)].preset;
					}

					_userDataService.AddDailyLevelItem(currentDateTicks, levelID, CalendarItemView.STATE_ACTIVE);
				}

				_userDataService.SetPlayedDailyDateTicks(currentDateTicks);

				switch ((GameModeTypes)levelsDaily[levelID].mode)
				{
					case GameModeTypes.Docku:
						_stateController.GoToState(AppState.DockuGame, new DockuGameStatePayload
						{
							LobbyType = LobbyTypes.Daily
						}).Forget();
						break;

					case GameModeTypes.Puzzle:
						_stateController.GoToState(AppState.PuzzleGame, new PuzzleGameStatePayload
						{
							LobbyType = LobbyTypes.Daily
						}).Forget();
						break;

					default:
						throw new NotImplementedException(LobbyExceptionMessage.NOT_IMPLEMENTED_GAMEMODE);
				}
			}
			else
			{
				Debug.LogError("[LobbyDailyService] CreateOrGetDailyLevelId - Table not found");
			}
		}

		public int GetDailyLevelState(int day)
		{
			int dateTicks = (int)TimestampUtil.GetTimestamp(_currentDateTime.Date.AddDays(day - _currentDateTime.Day));

			if (TryGetCreatedDailyLevelItem(dateTicks, out DailyLevelItem dailyItem))
			{
				return dailyItem.State;
			}

			return CalendarItemView.STATE_ACTIVE;
		}

		public bool IsAllDaysCompletedInCurrentDate()
		{
			DateTime dateTime = _currentDateTime.StartOfMonth();
			int startDateTimeTicks = (int)TimestampUtil.GetTimestamp(dateTime);
			int dayInMonth = DateTime.DaysInMonth(dateTime.Year, dateTime.Month);

			for (int i = startDateTimeTicks; i < startDateTimeTicks + TICKS_IN_ONE_DAY * dayInMonth; i += TICKS_IN_ONE_DAY)
			{
				if (TryGetCreatedDailyLevelItem(i, out DailyLevelItem dailyItem))
				{
					if (dailyItem.State == CalendarItemView.STATE_ACTIVE)
					{
						return false;
					}
				}
				else
				{
					return false;
				}
			}

			return true;
		}

		public float GetCurrentDateProgress(bool previous = false)
		{
			DateTime dateTime = _currentDateTime.StartOfMonth();
			int startDateTimeTicks = (int)TimestampUtil.GetTimestamp(dateTime);
			int dayInMonth = DateTime.DaysInMonth(dateTime.Year, dateTime.Month);

			float progress = 0;
			float weightOneDay = 1f / dayInMonth;

			for (int i = startDateTimeTicks; i < startDateTimeTicks + TICKS_IN_ONE_DAY * dayInMonth; i += TICKS_IN_ONE_DAY)
			{
				if (TryGetCreatedDailyLevelItem(i, out DailyLevelItem dailyItem))
				{
					if (dailyItem.State == CalendarItemView.STATE_COMPLETED)
					{
						progress += weightOneDay;
					}
				}
			}

			if (previous && progress > 0)
			{
				progress -= weightOneDay;
			}

			return progress;
		}

		public int GetLastAvailableDayInCurrentMonth()
		{
			DateTime currentDateTime = _currentDateTime.StartOfMonth();
			DateTime nowDateTime = GetDateNow();

			int daysInMonth = DateTime.DaysInMonth(currentDateTime.Year, currentDateTime.Month);

			int viewDays = currentDateTime.CompareYearAndMonthTo(nowDateTime) ? nowDateTime.Day : daysInMonth;

			int startDateTimeTicks = (int)TimestampUtil.GetTimestamp(currentDateTime);
			int availableDayTicks = 0;

			for (int i = startDateTimeTicks; i < startDateTimeTicks + TICKS_IN_ONE_DAY * viewDays; i += TICKS_IN_ONE_DAY)
			{
				if (TryGetCreatedDailyLevelItem(i, out DailyLevelItem dailyItem))
				{
					if (dailyItem.State == CalendarItemView.STATE_ACTIVE)
					{
						availableDayTicks = i;
					}
				}
				else
				{
					availableDayTicks = i;
				}
			}

			if (availableDayTicks == 0)
			{ 
				return 0;
			}

			return TimestampUtil.UtcDateTimeFromTimestamp(availableDayTicks).Day;
		}

		private bool TryGetCreatedDailyLevelItem(int date, out DailyLevelItem dailyItem)
		{
			if (_userDataService.DailyLevelItems.TryGetValue(date, out dailyItem))
			{
				return true;
			}

			return false;
		}
	}
}
