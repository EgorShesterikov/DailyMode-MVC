using MiniIT.Framework;
using TMPro;
using UnityEngine;

namespace App
{
	public class CalendarItemView : LevelView
	{
		public const int STATE_ACTIVE = 0;
		public const int STATE_COMPLETED = 1;

		private const string TOMORROW_TEXT_KEY = "DATE_TOMORROW";

		private const float DEFAULT_STATE_SCALE = 1.25f;

		[SerializeField] private TextMeshProUGUI _infoText;

		[SerializeField] private GameObject _waitImage;
		[SerializeField] private TextMeshProUGUI _waitTimeText;
		[SerializeField] private GameObject _level;

		public void DisplayAsLevel(int day, string infoText, LevelState levelState)
		{
			ResetLevelStates();
			_level.SetActive(true);

			_infoText.text = infoText;

			_waitTimeText.text = string.Empty;
			_waitImage.gameObject.SetActive(false);

			Setup(day - 1, day, 0, false, levelState);

			gameObject.SetActive(true);
		}


		private void ResetLevelStates()
		{
			foreach (Transform state in _level.transform)
			{
				state.gameObject.SetActive(false);
				state.transform.localScale = new Vector3(DEFAULT_STATE_SCALE, DEFAULT_STATE_SCALE, 1);
			}
		}

		public void DisplayAsWaitDate(string time)
		{
			_level.SetActive(false);

			_infoText.text = LocaleManager.GetString(string.Format(TOMORROW_TEXT_KEY));

			_waitTimeText.text = time;
			_waitImage.gameObject.SetActive(true);

			gameObject.SetActive(true);
		}

		public void DisplayAsMonth(string month)
		{
			_level.SetActive(false);

			_infoText.text = string.Empty;

			_waitTimeText.text = month;
			_waitImage.gameObject.SetActive(true);

			gameObject.SetActive(true);
		}

		public void Hide()
		{
			gameObject.SetActive(false);
		}
	}
}
