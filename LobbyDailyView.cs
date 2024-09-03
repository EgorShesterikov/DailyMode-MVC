using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Doozy.Engine.Progress;
using Doozy.Engine.UI;
using MiniIT.CodeGenerator.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App
{
	[DoozyButtons]
	public partial class LobbyDailyView : MonoBehaviour
	{
		public event Action<int> OnChangeCurrentDay = delegate { };

		[SerializeField] private TextMeshProUGUI _currentDateText;

		[SerializeField] private Progressor _cupProgress;
		[SerializeField] private GameObject _cupComplete;

		[SerializeField] private UIButton _arrowLeftButton;
		[SerializeField] private UIButton _arrowRightButton;

		[SerializeField] private CalendarItemView[] _calendarItems;
		[SerializeField] private ActionScrollRect _calendarScrollRect;
		[SerializeField] private HorizontalLayoutGroup _calendarContent;

		[SerializeField] private TextMeshProUGUI _currentLevelInfoPanel;

		[SerializeField] private UIButton _playButtonDaily;
		[SerializeField] private UIButton _playAdsButtonDaily;

		private int _activeObjectsInContentCount;

		private CalendarItemView _currentItemView;
		private int _currentDay;

		private float _calendarScrollRectWidth;
		private float _calendarItemWidth;

		private Image _arrowLeftImage;
		private Image _arrowRightImage;

		private Tween _scrollTween;
		private Sequence _completedSequence;

		private CancellationTokenSource _cancellationTokenSource;

		private void Awake()
		{
			_calendarScrollRectWidth = _calendarScrollRect.GetComponent<RectTransform>().rect.width;
			_calendarItemWidth = _calendarItems[0].GetComponent<RectTransform>().rect.width;

			_arrowLeftImage = _arrowLeftButton.GetComponent<Image>();
			_arrowRightImage = _arrowRightButton.GetComponent<Image>();
		}

		private void OnEnable()
		{
			_calendarScrollRect.onValueChanged.AddListener(DisplayCurrentDay);
			_calendarScrollRect.OnEndDragEvent += MoveToCurrentLevel;
			_calendarScrollRect.OnBeginDragEvent += KillActiveScrollTween;

			_cancellationTokenSource = new CancellationTokenSource();
		}

		private void OnDisable()
		{
			_calendarScrollRect.onValueChanged.RemoveListener(DisplayCurrentDay);
			_calendarScrollRect.OnEndDragEvent -= MoveToCurrentLevel;
			_calendarScrollRect.OnBeginDragEvent -= KillActiveScrollTween;

			_cancellationTokenSource.Cancel();
			_cancellationTokenSource.Dispose();

			KillActiveScrollTween();
			KillActiveCompletedSequence();
		}

		public void ResetParams()
		{
			_cancellationTokenSource.Cancel();
			_cancellationTokenSource = new CancellationTokenSource();

			_currentItemView = null;
			_currentDay = 0;
		}

		public void SetCurrentDate(string text)
		{
			_currentDateText.text = text;
		}

		public void SetCurrentDateInInfoPanel(string text)
		{
			_currentLevelInfoPanel.text = text;
		}

		public void SetProgressOfMonth(float progress, bool instantAction = false, bool completedAnim = false)
		{
			bool isCompleted = Mathf.Approximately(progress, 1);

			if (!isCompleted)
			{
				SetProcessProgressView();
			}
			else
			{
				if (completedAnim)
				{
					ShowCompletedMonthAsync().Forget();
				}
				else
				{
					SetCompletedProgressView();
				}
			}

			_cupProgress.SetValue(progress, instantAction);
		}

		private async UniTaskVoid ShowCompletedMonthAsync()
		{
			await UniTask.WaitUntil(() => Mathf.Approximately(_cupProgress.Value, 1), cancellationToken: _cancellationTokenSource.Token);

			SetCompletedProgressView();

			KillActiveCompletedSequence();

			const float CUP_COMPLETED_MAX_SCALE = 1.15f;
			const float CUP_COMPLETED_DO_MAX_SCALE_DURATION = 0.5f;

			const float CUP_COMPLETED_DEFAULT_SCALE = 1f;
			const float CUP_COMPLETED_DO_DEFAULT_SCALE_DURATION = 0.5f;

			_completedSequence = DOTween.Sequence();
			_completedSequence.SetLink(_cupComplete.gameObject)
				.Append(_cupComplete.transform.DOScale(CUP_COMPLETED_MAX_SCALE, CUP_COMPLETED_DO_MAX_SCALE_DURATION))
				.Append(_cupComplete.transform.DOScale(CUP_COMPLETED_DEFAULT_SCALE, CUP_COMPLETED_DO_DEFAULT_SCALE_DURATION));
		}

		private void SetProcessProgressView()
		{
			_cupProgress.gameObject.SetActive(true);
			_cupComplete.SetActive(false);
		}

		private void SetCompletedProgressView()
		{
			_cupProgress.gameObject.SetActive(false);
			_cupComplete.SetActive(true);
		}

		public void SetCompletedDayAnim(int day, Action callBack = null)
		{
			_calendarItems[day].PlayLevelEndAnimation(callBack);
		}

		public void SetCalendarItemAsPreviousMonth(string month)
		{
			_calendarItems[0].DisplayAsMonth(month);
		}

		public void SetCalendarItemAsNextMonth(string month)
		{
			_calendarItems[^1].DisplayAsMonth(month);
		}

		public void SetCalendarItemAsLevel(int day, string infoText, LevelState levelState)
		{
			_calendarItems[day].DisplayAsLevel(day, infoText, levelState);
		}

		public void SetCalendarItemAsWaitForDate(string time)
		{
			_calendarItems[^1].DisplayAsWaitDate(time);
		}

		public void HideCalendarItem(int day)
		{
			_calendarItems[day].Hide();
		}

		public async UniTaskVoid DelayedScrollCalendarToTargetAsync(float delayedSeconds, int day, bool instantAction = false, Action callBack = null)
		{
			float cacheNormalizedPosition = _calendarScrollRect.normalizedPosition.x;

			await UniTask.WaitForSeconds(delayedSeconds, cancellationToken: _cancellationTokenSource.Token);

			if (Mathf.Approximately(cacheNormalizedPosition, _calendarScrollRect.normalizedPosition.x))
			{
				ScrollCalendarToTarget(day, instantAction, callBack);
			}
		}

		public void ScrollCalendarToTarget(int day, bool instantAction = false, Action callBack = null)
		{
			int index = 1 + day;

			float contentWidth = _activeObjectsInContentCount * _calendarItemWidth + (_activeObjectsInContentCount - 1) * _calendarContent.spacing;

			float elementPosition = index * (_calendarItemWidth + _calendarContent.spacing);
			float offset = (_calendarScrollRectWidth - _calendarItemWidth) * 0.5f;

			float normalizedPosition = (elementPosition - offset) / (contentWidth - _calendarScrollRectWidth);
			float targetHorizontalNormalizedPosition = Mathf.Clamp01(normalizedPosition);

			KillActiveScrollTween();

			if (Mathf.Approximately(_calendarScrollRect.horizontalNormalizedPosition, targetHorizontalNormalizedPosition))
			{
				DisplayCurrentDay(_calendarScrollRect.normalizedPosition);
				callBack?.Invoke();
			}
			else
			{
				if (instantAction)
				{
					_calendarScrollRect.horizontalNormalizedPosition = targetHorizontalNormalizedPosition;
					callBack?.Invoke();
				}
				else
				{
					const float SCROLL_CALENDAR_TO_TARGET_DURATION = 0.5f;

					_scrollTween = DOTween.To(() => _calendarScrollRect.horizontalNormalizedPosition,
							value => _calendarScrollRect.horizontalNormalizedPosition = value,
							targetHorizontalNormalizedPosition, SCROLL_CALENDAR_TO_TARGET_DURATION)
						.SetEase(Ease.OutExpo)
						.OnComplete(() => callBack?.Invoke());
				}
			}
		}

		public void SetActiveObjectsInContentCount(int value)
		{
			_activeObjectsInContentCount = value;
		}

		private void DisplayCurrentDay(Vector2 _)
		{
			_currentDay = GetCenterLevel();

			if (_currentItemView != _calendarItems[_currentDay])
			{
				if (_currentItemView != null)
				{
					_currentItemView.ChangeLevelState(false);
				}

				_currentItemView = _calendarItems[_currentDay];
				_currentItemView.ChangeLevelState(true);

				OnChangeCurrentDay.Invoke(_currentDay);
			}
		}

		private int GetCenterLevel()
		{
			float contentWidth = _activeObjectsInContentCount * _calendarItemWidth + (_activeObjectsInContentCount - 1) * _calendarContent.spacing;
			float offset = (_calendarScrollRectWidth - _calendarItemWidth) * 0.5f;

			int index = (int)((_calendarScrollRect.horizontalNormalizedPosition * (contentWidth - _calendarScrollRectWidth) + offset) / (_calendarItemWidth + _calendarContent.spacing) - 0.5f);
			int nonGameObjects = 4;

			return Mathf.Clamp(index, 1, _activeObjectsInContentCount - nonGameObjects);
		}

		private void KillActiveScrollTween()
		{
			_scrollTween?.Kill();
		}

		private void KillActiveCompletedSequence()
		{
			_completedSequence?.Kill();
			_cupProgress.transform.localScale = Vector3.one;
		}

		private void MoveToCurrentLevel()
		{
			ScrollCalendarToTarget(_currentDay);
		}

		public void DisplayPlayButton()
		{
			_playButtonDaily.gameObject.SetActive(true);
			_playAdsButtonDaily.gameObject.SetActive(false);
		}

		public void DisplayAdsPlayButton()
		{
			_playButtonDaily.gameObject.SetActive(false);
			_playAdsButtonDaily.gameObject.SetActive(true);
		}

		public void SetActiveLeftArrowButton(bool value)
		{
			_arrowLeftImage.enabled = value;
		}

		public void SetActiveRightArrowButton(bool value)
		{
			_arrowRightImage.enabled = value;
		}

		public void SetEnabledScrollRect(bool value)
		{
			_calendarScrollRect.enabled = value;
		}

		public void SetInteractablePlayAdsButton(bool value)
		{
			_playAdsButtonDaily.Interactable = value;
		}
	}
}
