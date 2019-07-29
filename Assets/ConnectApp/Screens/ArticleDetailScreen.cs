using System;
using System.Collections.Generic;
using ConnectApp.Components;
using ConnectApp.Components.pull_to_refresh;
using ConnectApp.Constants;
using ConnectApp.Main;
using ConnectApp.Models.ActionModel;
using ConnectApp.Models.Model;
using ConnectApp.Models.State;
using ConnectApp.Models.ViewModel;
using ConnectApp.redux.actions;
using ConnectApp.Utils;
using RSG;
using Unity.UIWidgets.animation;
using Unity.UIWidgets.foundation;
using Unity.UIWidgets.painting;
using Unity.UIWidgets.rendering;
using Unity.UIWidgets.Redux;
using Unity.UIWidgets.scheduler;
using Unity.UIWidgets.service;
using Unity.UIWidgets.ui;
using Unity.UIWidgets.widgets;
using UnityEngine;
using Avatar = ConnectApp.Components.Avatar;

namespace ConnectApp.screens {
    public class ArticleDetailScreenConnector : StatelessWidget {
        public ArticleDetailScreenConnector(
            string articleId,
            bool isPush = false,
            Key key = null
        ) : base(key: key) {
            this.articleId = articleId;
            this.isPush = isPush;
        }

        readonly string articleId;
        readonly bool isPush;

        public override Widget build(BuildContext context) {
            return new StoreConnector<AppState, ArticleDetailScreenViewModel>(
                converter: state => new ArticleDetailScreenViewModel {
                    articleId = this.articleId,
                    loginUserId = state.loginState.loginInfo.userId,
                    isLoggedIn = state.loginState.isLoggedIn,
                    articleDetailLoading = state.articleState.articleDetailLoading,
                    articleDict = state.articleState.articleDict,
                    channelMessageList = state.messageState.channelMessageList,
                    channelMessageDict = state.messageState.channelMessageDict,
                    userDict = state.userState.userDict,
                    teamDict = state.teamState.teamDict,
                    followMap = state.followState.followDict.ContainsKey(state.loginState.loginInfo.userId ?? "")
                        ? state.followState.followDict[state.loginState.loginInfo.userId ?? ""]
                        : new Dictionary<string, bool>()
                },
                builder: (context1, viewModel, dispatcher) => {
                    var actionModel = new ArticleDetailScreenActionModel {
                        mainRouterPop = () => dispatcher.dispatch(new MainNavigatorPopAction()),
                        pushToLogin = () => dispatcher.dispatch(new MainNavigatorPushToAction {
                            routeName = MainNavigatorRoutes.Login
                        }),
                        openUrl = url => dispatcher.dispatch(new MainNavigatorPushToWebViewAction {
                            url = url
                        }),
                        playVideo = url => dispatcher.dispatch(new PlayVideoAction {
                            url = url
                        }),
                        pushToArticleDetail = id => dispatcher.dispatch(
                            new MainNavigatorPushToArticleDetailAction {
                                articleId = id
                            }
                        ),
                        pushToUserDetail = userId => dispatcher.dispatch(
                            new MainNavigatorPushToUserDetailAction {
                                userId = userId
                            }
                        ),
                        pushToTeamDetail = teamId => dispatcher.dispatch(
                            new MainNavigatorPushToTeamDetailAction {
                                teamId = teamId
                            }
                        ),
                        pushToReport = (reportId, reportType) => dispatcher.dispatch(
                            new MainNavigatorPushToReportAction {
                                reportId = reportId,
                                reportType = reportType
                            }
                        ),
                        pushToBlock = articleId => {
                            dispatcher.dispatch(new BlockArticleAction {articleId = articleId});
                            dispatcher.dispatch(new DeleteArticleHistoryAction {articleId = articleId});
                        },
                        startFetchArticleDetail = () => dispatcher.dispatch(new StartFetchArticleDetailAction()),
                        fetchArticleDetail = id =>
                            dispatcher.dispatch<IPromise>(Actions.FetchArticleDetail(id, this.isPush)),
                        fetchArticleComments = (channelId, currOldestMessageId) =>
                            dispatcher.dispatch<IPromise>(
                                Actions.fetchArticleComments(channelId, currOldestMessageId)
                            ),
                        likeArticle = id => {
                            AnalyticsManager.ClickLike("Article", this.articleId);
                            return dispatcher.dispatch<IPromise>(Actions.likeArticle(id));
                        },
                        likeComment = message => {
                            AnalyticsManager.ClickLike("Article_Comment", this.articleId, message.id);
                            return dispatcher.dispatch<IPromise>(Actions.likeComment(message));
                        },
                        removeLikeComment =
                            message => {
                                AnalyticsManager.ClickLike("Article_Remove_Comment", this.articleId, message.id);
                                return dispatcher.dispatch<IPromise>(Actions.removeLikeComment(message));
                            },
                        sendComment = (channelId, content, nonce, parentMessageId) => {
                            AnalyticsManager.ClickPublishComment(
                                parentMessageId == null ? "Article" : "Article_Comment", channelId, parentMessageId);
                            CustomDialogUtils.showCustomDialog(child: new CustomLoadingDialog());
                            return dispatcher.dispatch<IPromise>(
                                Actions.sendComment(this.articleId, channelId, content, nonce, parentMessageId));
                        },
                        startFollowUser = userId =>
                            dispatcher.dispatch(new StartFetchFollowUserAction {followUserId = userId}),
                        followUser = userId => dispatcher.dispatch<IPromise>(Actions.fetchFollowUser(userId)),
                        startUnFollowUser = userId =>
                            dispatcher.dispatch(new StartFetchUnFollowUserAction {unFollowUserId = userId}),
                        unFollowUser = userId => dispatcher.dispatch<IPromise>(Actions.fetchUnFollowUser(userId)),
                        startFollowTeam = teamId =>
                            dispatcher.dispatch(new StartFetchFollowTeamAction {followTeamId = teamId}),
                        followTeam = teamId => dispatcher.dispatch<IPromise>(Actions.fetchFollowTeam(teamId)),
                        startUnFollowTeam = teamId =>
                            dispatcher.dispatch(new StartFetchUnFollowTeamAction {unFollowTeamId = teamId}),
                        unFollowTeam = teamId => dispatcher.dispatch<IPromise>(Actions.fetchUnFollowTeam(teamId)),
                        shareToWechat = (type, title, description, linkUrl, imageUrl) => dispatcher.dispatch<IPromise>(
                            Actions.shareToWechat(type, title, description, linkUrl, imageUrl))
                    };
                    return new ArticleDetailScreen(viewModel, actionModel);
                }
            );
        }
    }

    class ArticleDetailScreen : StatefulWidget {
        public ArticleDetailScreen(
            ArticleDetailScreenViewModel viewModel = null,
            ArticleDetailScreenActionModel actionModel = null,
            Key key = null
        ) : base(key: key) {
            this.viewModel = viewModel;
            this.actionModel = actionModel;
        }

        public readonly ArticleDetailScreenViewModel viewModel;
        public readonly ArticleDetailScreenActionModel actionModel;

        public override State createState() {
            return new _ArticleDetailScreenState();
        }
    }

    enum _ArticleJumpToCommentState {
        Inactive,
        active
    }

    class _ArticleDetailScreenState : State<ArticleDetailScreen>, TickerProvider, RouteAware {
        const float navBarHeight = 44;
        Article _article = new Article();
        User _user = new User();
        Team _team = new Team();
        bool _isHaveTitle;
        float _titleHeight;
        Animation<RelativeRect> _animation;
        AnimationController _controller;
        RefreshController _refreshController;
        string _loginSubId;
        _ArticleJumpToCommentState _jumpState;

        float? _cachedCommentPosition;

        public override void initState() {
            base.initState();
            StatusBarManager.statusBarStyle(false);
            this._refreshController = new RefreshController();
            this._isHaveTitle = false;
            this._titleHeight = 0.0f;
            this._controller = new AnimationController(
                duration: TimeSpan.FromMilliseconds(100),
                vsync: this
            );
            RelativeRectTween rectTween = new RelativeRectTween(
                RelativeRect.fromLTRB(0, navBarHeight, 0, 0),
                RelativeRect.fromLTRB(0, 13, 0, 0)
            );
            this._animation = rectTween.animate(this._controller);
            SchedulerBinding.instance.addPostFrameCallback(_ => {
                this.widget.actionModel.startFetchArticleDetail();
                this.widget.actionModel.fetchArticleDetail(arg: this.widget.viewModel.articleId);
            });
            this._loginSubId = EventBus.subscribe(sName: EventBusConstant.login_success, args => {
                this.widget.actionModel.startFetchArticleDetail();
                this.widget.actionModel.fetchArticleDetail(arg: this.widget.viewModel.articleId);
            });
            this._jumpState = _ArticleJumpToCommentState.Inactive;
            this._cachedCommentPosition = null;
        }
        
        public override void didChangeDependencies() {
            base.didChangeDependencies();
            Router.routeObserve.subscribe(this, (PageRoute) ModalRoute.of(this.context));
        }

        public override void dispose() {
            EventBus.unSubscribe(sName: EventBusConstant.login_success, id: this._loginSubId);
            Router.routeObserve.unsubscribe(this);
            base.dispose();
        }

        public Ticker createTicker(TickerCallback onTick) {
            return new Ticker(onTick: onTick, () => $"created by {this}");
        }

        public override Widget build(BuildContext context) {
            this.widget.viewModel.articleDict.TryGetValue(key: this.widget.viewModel.articleId,
                value: out this._article);
            if (this.widget.viewModel.articleDetailLoading && (this._article == null || !this._article.isNotFirst)) {
                return new Container(
                    color: CColors.White,
                    child: new CustomSafeArea(
                        child: new Column(
                            children: new List<Widget> {
                                this._buildNavigationBar(false),
                                new ArticleDetailLoading()
                            }
                        )
                    )
                );
            }

            if (this._article == null || this._article.channelId == null) {
                return new Container();
            }

            if (this._article.ownerType == "user") {
                if (this._article.userId != null &&
                    this.widget.viewModel.userDict.TryGetValue(this._article.userId, out this._user)) {
                    this._user = this.widget.viewModel.userDict[key: this._article.userId];
                }
            }

            if (this._article.ownerType == "team") {
                if (this._article.teamId != null &&
                    this.widget.viewModel.teamDict.TryGetValue(this._article.teamId, out this._team)) {
                    this._team = this.widget.viewModel.teamDict[key: this._article.teamId];
                }
            }

            if (this._titleHeight == 0f && this._article.title.isNotEmpty()) {
                this._titleHeight = CTextUtils.CalculateTextHeight(
                                        text: this._article.title,
                                        textStyle: CTextStyle.H3,
                                        MediaQuery.of(context).size.width - 16 * 2, // 16 is horizontal padding
                                        null
                                    ) + 16; // 16 is top padding
                this.setState(() => { });
            }

            var commentIndex = 0;
            var originItems = this._article == null ? new List<Widget>() : this._buildItems(context, out commentIndex);
            commentIndex = this._jumpState == _ArticleJumpToCommentState.active ? commentIndex : 0;
            this._jumpState = _ArticleJumpToCommentState.Inactive;

            var child = new Container(
                color: CColors.Background,
                child: new Column(
                    children: new List<Widget> {
                        this._buildNavigationBar(),
                        new Expanded(
                            child: new CustomScrollbar(
                                new CenteredRefresher(
                                    controller: this._refreshController,
                                    enablePullDown: false,
                                    enablePullUp: this._article.hasMore,
                                    onRefresh: this._onRefresh,
                                    onNotification: this._onNotification,
                                    children: originItems,
                                    centerIndex: commentIndex
                                )
                            )
                        ),
                        this._buildArticleTabBar()
                    }
                )
            );
            return new Container(
                color: CColors.White,
                child: new CustomSafeArea(
                    child: child
                )
            );
        }

        List<Widget> _buildItems(BuildContext context, out int commentIndex) {
            var originItems = new List<Widget> {
                this._buildContentHead()
            };
            originItems.AddRange(
                ContentDescription.map(
                    context: context,
                    cont: this._article.body,
                    contentMap: this._article.contentMap,
                    openUrl: this.widget.actionModel.openUrl,
                    playVideo: this.widget.actionModel.playVideo
                )
            );
            // originItems.Add(this._buildActionCards(this._article.like));
            originItems.Add(this._buildRelatedArticles());
            commentIndex = originItems.Count;
            originItems.AddRange(this._buildComments(context: context));

            return originItems;
        }

        Widget _buildNavigationBar(bool isShowRightWidget = true) {
            Widget titleWidget = new Container();
            if (this._isHaveTitle) {
                titleWidget = new Text(
                    this._article.title,
                    style: CTextStyle.PXLargeMedium,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    textAlign: TextAlign.center
                );
            }

            Widget rightWidget = new Container();
            if (isShowRightWidget) {
                string rightWidgetTitle = this._article.commentCount > 0
                    ? $"{this._article.commentCount} 评论"
                    : "抢个沙发";
                rightWidget = new Container(
                    margin: EdgeInsets.only(8, right: 16),
                    child: new CustomButton(
                        padding: EdgeInsets.zero,
                        onPressed: () => {
                            //do not jump if we are already at the exact comment position
                            if (this._refreshController.scrollController.position.pixels ==
                                this._cachedCommentPosition) {
                                return;
                            }

                            //first frame: create a new scroll view in which the center of the viewport is the comment widget
                            this.setState(
                                () => { this._jumpState = _ArticleJumpToCommentState.active; });

                            SchedulerBinding.instance.addPostFrameCallback((TimeSpan value2) => {
                                //calculate the comment position = curPixel(0) - minScrollExtent
                                var commentPosition = -this._refreshController.scrollController.position
                                    .minScrollExtent;

                                //cache the current comment position  
                                this._cachedCommentPosition = commentPosition;

                                //second frame: create a new scroll view which starts from the default first widget
                                //and then jump to the calculated comment position
                                this.setState(() => {
                                    this._refreshController.scrollController.jumpTo(commentPosition);

                                    //assume that when we jump to the comment, the title should always be shown as the header
                                    //this assumption will fail when an article is shorter than 16 pixels in height (as referred to in _onNotification
                                    this._controller.forward();
                                    this._isHaveTitle = true;
                                });
                            });
                        },
                        child: new Container(
                            height: 28,
                            padding: EdgeInsets.symmetric(horizontal: 16),
                            alignment: Alignment.center,
                            decoration: new BoxDecoration(
                                color: CColors.PrimaryBlue,
                                borderRadius: BorderRadius.all(14)
                            ),
                            child: new Text(
                                data: rightWidgetTitle,
                                style: new TextStyle(
                                    fontSize: 14,
                                    fontFamily: "Roboto-Medium",
                                    color: CColors.White
                                )
                            )
                        )
                    )
                );
            }

            return new CustomAppBar(
                () => this.widget.actionModel.mainRouterPop(),
                new Expanded(
                    child: new Stack(
                        fit: StackFit.expand,
                        children: new List<Widget> {
                            new PositionedTransition(
                                rect: this._animation,
                                child: titleWidget
                            )
                        }
                    )
                ),
                rightWidget: rightWidget,
                this._isHaveTitle ? CColors.Separator2 : CColors.Transparent
            );
        }

        Widget _buildArticleTabBar() {
            return new ArticleTabBar(
                like: this._article.like,
                () => this._comment("Article"),
                () => this._comment("Article"),
                () => {
                    if (!this.widget.viewModel.isLoggedIn) {
                        this.widget.actionModel.pushToLogin();
                    }
                    else {
                        if (!this._article.like) {
                            this.widget.actionModel.likeArticle(this._article.id);
                        }
                    }
                },
                shareCallback: this.share
            );
        }

        void _onRefresh(bool up) {
            if (!up) {
                this.widget.actionModel.fetchArticleComments(this._article.channelId, this._article.currOldestMessageId)
                    .Then(() => { this._refreshController.sendBack(up, RefreshStatus.idle); })
                    .Catch(err => { this._refreshController.sendBack(up, RefreshStatus.failed); });
            }
        }

        void _onFollow(UserType userType, string userId) {
            if (this.widget.viewModel.isLoggedIn) {
                if (userType == UserType.follow) {
                    ActionSheetUtils.showModalActionSheet(
                        new ActionSheet(
                            title: "确定不再关注？",
                            items: new List<ActionSheetItem> {
                                new ActionSheetItem("确定", type: ActionType.normal, () => {
                                    if (this._article.ownerType == OwnerType.user.ToString()) {
                                        this.widget.actionModel.startUnFollowUser(obj: userId);
                                        this.widget.actionModel.unFollowUser(arg: userId);
                                    }

                                    if (this._article.ownerType == OwnerType.team.ToString()) {
                                        this.widget.actionModel.startUnFollowTeam(obj: userId);
                                        this.widget.actionModel.unFollowTeam(arg: userId);
                                    }
                                }),
                                new ActionSheetItem("取消", type: ActionType.cancel)
                            }
                        )
                    );
                }

                if (userType == UserType.unFollow) {
                    if (this._article.ownerType == OwnerType.user.ToString()) {
                        this.widget.actionModel.startFollowUser(obj: userId);
                        this.widget.actionModel.followUser(arg: userId);
                    }

                    if (this._article.ownerType == OwnerType.team.ToString()) {
                        this.widget.actionModel.startFollowTeam(obj: userId);
                        this.widget.actionModel.followTeam(arg: userId);
                    }
                }
            }
            else {
                this.widget.actionModel.pushToLogin();
            }
        }

        bool _onNotification(ScrollNotification notification) {
            var pixels = notification.metrics.pixels - notification.metrics.minScrollExtent;
            if (pixels > this._titleHeight) {
                if (this._isHaveTitle == false) {
                    this._controller.forward();
                    this.setState(() => this._isHaveTitle = true);
                }
            }
            else {
                if (this._isHaveTitle) {
                    this._controller.reverse();
                    this.setState(() => this._isHaveTitle = false);
                }
            }

            return true;
        }

        Widget _buildContentHead() {
            Widget _avatar = this._article.ownerType == OwnerType.user.ToString()
                ? Avatar.User(user: this._user, 32)
                : Avatar.Team(team: this._team, 32);

            var text = this._article.ownerType == "user"
                ? this._user.fullName ?? this._user.name
                : this._team.name;
            var description = this._article.ownerType == "user" ? this._user.title : "";
            var time = this._article.publishedTime;
            Widget descriptionWidget = new Container();
            if (description.isNotEmpty()) {
                descriptionWidget = new Text(
                    data: description,
                    style: CTextStyle.PSmallBody3
                );
            }

            return new Container(
                color: CColors.White,
                padding: EdgeInsets.only(16, 16, 16),
                child: new Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: new List<Widget> {
                        new Text(
                            data: this._article.title,
                            style: CTextStyle.H3
                        ),
                        new Container(
                            margin: EdgeInsets.only(top: 8),
                            child: new Text(
                                $"阅读 {this._article.viewCount} · {DateConvert.DateStringFromNow(dt: time)}",
                                style: CTextStyle.PSmallBody4
                            )
                        ),
                        new Row(
                            children: new List<Widget> {
                                new Expanded(
                                    child: new GestureDetector(
                                        onTap: () => {
                                            if (this._article.ownerType == OwnerType.user.ToString()) {
                                                this.widget.actionModel.pushToUserDetail(obj: this._user.id);
                                            }

                                            if (this._article.ownerType == OwnerType.team.ToString()) {
                                                this.widget.actionModel.pushToTeamDetail(obj: this._team.id);
                                            }
                                        },
                                        child: new Container(
                                            margin: EdgeInsets.only(top: 24, bottom: 24),
                                            color: CColors.Transparent,
                                            child: new Row(
                                                mainAxisSize: MainAxisSize.min,
                                                children: new List<Widget> {
                                                    new Container(
                                                        margin: EdgeInsets.only(right: 8),
                                                        child: _avatar
                                                    ),
                                                    new Column(
                                                        mainAxisAlignment: MainAxisAlignment.center,
                                                        crossAxisAlignment: CrossAxisAlignment.start,
                                                        children: new List<Widget> {
                                                            new Text(
                                                                data: text,
                                                                style: CTextStyle.PRegularBody
                                                            ),
                                                            descriptionWidget
                                                        }
                                                    )
                                                }
                                            )
                                        )
                                    )
                                ),
                                new SizedBox(width: 8),
                                this._buildFollowButton()
                            }
                        ),
                        this._article.subTitle.isEmpty()
                            ? new Container()
                            : new Container(
                                margin: EdgeInsets.only(bottom: 24),
                                decoration: new BoxDecoration(
                                    color: CColors.Separator2,
                                    borderRadius: BorderRadius.all(4)
                                ),
                                padding: EdgeInsets.only(16, 12, 16, 12),
                                width: Screen.width - 32,
                                child: new Text($"{this._article.subTitle}", style: CTextStyle.PLargeBody4)
                            )
                    }
                )
            );
        }

        Widget _buildFollowButton() {
            var id = this._article.ownerType == OwnerType.user.ToString() ? this._user.id : this._team.id;
            UserType userType = UserType.unFollow;
            if (!this.widget.viewModel.isLoggedIn) {
                userType = UserType.unFollow;
            }
            else {
                bool? followLoading = this._article.ownerType == OwnerType.user.ToString()
                    ? this._user.followUserLoading
                    : this._team.followTeamLoading;
                if (this.widget.viewModel.loginUserId == id) {
                    userType = UserType.me;
                }
                else if (followLoading ?? false) {
                    userType = UserType.loading;
                }
                else if (this.widget.viewModel.followMap.ContainsKey(key: id)) {
                    userType = UserType.follow;
                }
            }

            return new FollowButton(
                userType: userType,
                () => this._onFollow(userType: userType, userId: id)
            );
        }

        Widget _buildActionCards(bool like) {
            return new Container(
                color: CColors.White,
                padding: EdgeInsets.only(bottom: 40),
                child: new Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    crossAxisAlignment: CrossAxisAlignment.center,
                    children: new List<Widget> {
                        new ActionCard(like ? Icons.favorite : Icons.favorite_border, like ? "已赞" : "点赞", like, () => {
                            if (!this.widget.viewModel.isLoggedIn) {
                                this.widget.actionModel.pushToLogin();
                            }
                            else {
                                if (!like) {
                                    this.widget.actionModel.likeArticle(this._article.id);
                                }
                            }
                        }),
                        new Container(width: 16),
                        new ActionCard(Icons.share, "分享", false, this.share)
                    }
                )
            );
        }

        Widget _buildRelatedArticles() {
            if (this._article.projectIds == null || this._article.projectIds.Count == 0) {
                return new Container();
            }

            var widgets = new List<Widget>();
            this._article.projectIds.ForEach(articleId => {
                var article = this.widget.viewModel.articleDict[key: articleId];
                //对文章进行过滤
                if (article.id != this._article.id) {
                    var fullName = "";
                    if (article.ownerType == OwnerType.user.ToString()) {
                        fullName = this._user.fullName ?? this._user.name;
                    }

                    if (article.ownerType == OwnerType.team.ToString()) {
                        fullName = this._team.name;
                    }

                    Widget card = new RelatedArticleCard(
                        article: article,
                        fullName: fullName,
                        () => {
                            AnalyticsManager.ClickEnterArticleDetail(
                                "ArticleDetail_Related",
                                articleId: article.id,
                                articleTitle: article.title
                            );
                            this.widget.actionModel.pushToArticleDetail(obj: article.id);
                        },
                        key: new ObjectKey(value: article.id)
                    );
                    widgets.Add(item: card);
                }
            });
            if (widgets.isNotEmpty()) {
                widgets.InsertRange(0, new List<Widget> {
                    new Container(
                        height: 1,
                        color: CColors.Separator2,
                        margin: EdgeInsets.only(16, 16, 16, 40)
                    ),
                    new Container(
                        margin: EdgeInsets.only(16, bottom: 16),
                        child: new Text(
                            "推荐阅读",
                            style: CTextStyle.PLargeMedium
                        )
                    )
                });
            }

            return new Container(
                color: CColors.White,
                margin: EdgeInsets.only(bottom: 16),
                child: new Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: widgets
                )
            );
        }

        IEnumerable<Widget> _buildComments(BuildContext context) {
            List<string> channelComments = new List<string>();
            if (this.widget.viewModel.channelMessageList.ContainsKey(key: this._article.channelId)) {
                channelComments = this.widget.viewModel.channelMessageList[key: this._article.channelId];
            }

            var mediaQuery = MediaQuery.of(context);
            var comments = new List<Widget> {
                new Container(
                    color: CColors.White,
                    width: mediaQuery.size.width,
                    padding: EdgeInsets.only(16, 16, 16),
                    child: new Text(
                        "评论",
                        style: CTextStyle.H5,
                        textAlign: TextAlign.left
                    )
                )
            };

            var titleHeight = CTextUtils.CalculateTextHeight(
                                  "评论",
                                  CTextStyle.H5,
                                  mediaQuery.size.width - 16 * 2, // 16 is horizontal padding
                                  null
                              ) + 16; // 16 is top padding

            float safeAreaPadding = 0;
            if (Application.platform != RuntimePlatform.Android) {
                safeAreaPadding = mediaQuery.padding.vertical;
            }

            var height = mediaQuery.size.height - navBarHeight - 44 - safeAreaPadding;
            if (channelComments.Count == 0) {
                var blankView = new Container(
                    height: height - titleHeight,
                    child: new BlankView(
                        "快来写下第一条评论吧",
                        "image/default-comment"
                    )
                );
                comments.Add(item: blankView);
                return comments;
            }

            var messageDict = this.widget.viewModel.channelMessageDict[key: this._article.channelId];
            float contentHeights = 0;
            foreach (var commentId in channelComments) {
                if (!messageDict.ContainsKey(key: commentId)) {
                    break;
                }

                var message = messageDict[key: commentId];
                bool isPraised = _isPraised(message: message, loginUserId: this.widget.viewModel.loginUserId);
                var parentName = "";
                var parentAuthorId = "";
                if (message.parentMessageId.isNotEmpty()) {
                    if (messageDict.ContainsKey(key: message.parentMessageId)) {
                        var parentMessage = messageDict[key: message.parentMessageId];
                        parentName = parentMessage.author.fullName;
                        parentAuthorId = parentMessage.author.id;
                    }
                }

                var content = MessageUtils.AnalyzeMessage(message.content, message.mentions,
                                  message.mentionEveryone) + (parentName.isEmpty() ? "" : $"回复@{parentName}");
                var contentHeight = CTextUtils.CalculateTextHeight(
                                        content,
                                        CTextStyle.PLargeBody,
                                        // 16 is horizontal padding, 24 is avatar size, 8 is content left margin to avatar
                                        mediaQuery.size.width - 16 * 2 - 24 - 8,
                                        null
                                    ) + 16 + 24 + 3 + 5 + 22 + 12;
                // 16 is top padding, 24 is avatar size, 3 is content top margin to avatar, 5 is content bottom margin to commentTime
                // 22 is commentTime height, 12 is commentTime bottom margin
                contentHeights += contentHeight;
                var card = new CommentCard(
                    message: message,
                    isPraised: isPraised,
                    parentName: parentName,
                    parentAuthorId: parentAuthorId,
                    () => ReportManager.showReportView(
                        isLoggedIn: this.widget.viewModel.isLoggedIn,
                        reportType: ReportType.comment,
                        () => this.widget.actionModel.pushToLogin(),
                        () => this.widget.actionModel.pushToReport(commentId, ReportType.comment)
                    ),
                    replyCallBack: () => this._comment(
                        "Article_Comment",
                        commentId: commentId,
                        message.author.fullName.isEmpty() ? "" : message.author.fullName
                    ),
                    praiseCallBack: () => {
                        if (!this.widget.viewModel.isLoggedIn) {
                            this.widget.actionModel.pushToLogin();
                        }
                        else {
                            if (isPraised) {
                                this.widget.actionModel.removeLikeComment(arg: message);
                            }
                            else {
                                this.widget.actionModel.likeComment(arg: message);
                            }
                        }
                    },
                    pushToUserDetail: this.widget.actionModel.pushToUserDetail
                );
                comments.Add(item: card);
            }

            float endHeight = 0;
            if (!this._article.hasMore) {
                comments.Add(new EndView());
                endHeight = 52;
            }

            if (titleHeight + contentHeights + endHeight < height) {
                return new List<Widget> {
                    new Container(
                        height: height,
                        child: new Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: comments
                        )
                    )
                };
            }

            return comments;
        }

        static bool _isPraised(Message message, string loginUserId) {
            foreach (var reaction in message.reactions) {
                if (reaction.user.id == loginUserId) {
                    return true;
                }
            }

            return false;
        }

        void share() {
            var userId = "";
            if (this._article.ownerType == OwnerType.user.ToString()) {
                userId = this._article.userId;
            }

            if (this._article.ownerType == OwnerType.team.ToString()) {
                userId = this._article.teamId;
            }

            var linkUrl = CStringUtils.JointProjectShareLink(projectId: this._article.id);

            ShareManager.showArticleShareView(
                this.widget.viewModel.loginUserId != userId,
                isLoggedIn: this.widget.viewModel.isLoggedIn,
                () => {
                    Clipboard.setData(new ClipboardData(text: linkUrl));
                    CustomDialogUtils.showToast("复制链接成功", Icons.check_circle_outline);
                },
                () => this.widget.actionModel.pushToLogin(),
                () => this.widget.actionModel.pushToBlock(this._article.id),
                () => this.widget.actionModel.pushToReport(this._article.id, ReportType.article),
                type => {
                    CustomDialogUtils.showCustomDialog(
                        child: new CustomLoadingDialog()
                    );
                    string imageUrl = $"{this._article.thumbnail.url}.200x0x1.jpg";
                    this.widget.actionModel.shareToWechat(arg1: type, arg2: this._article.title,
                            arg3: this._article.subTitle, arg4: linkUrl, arg5: imageUrl)
                        .Then(onResolved: CustomDialogUtils.hiddenCustomDialog)
                        .Catch(_ => CustomDialogUtils.hiddenCustomDialog());
                },
                () => this.widget.actionModel.mainRouterPop()
            );
        }

        void _comment(string type, string commentId = "", string replyUserName = null) {
            if (!this.widget.viewModel.isLoggedIn) {
                this.widget.actionModel.pushToLogin();
            }
            else {
                AnalyticsManager.ClickComment(
                    type: type,
                    channelId: this._article.channelId,
                    title: this._article.title,
                    commentId: commentId
                );
                ActionSheetUtils.showModalActionSheet(new CustomInput(
                    replyUserName: replyUserName,
                    text => {
                        ActionSheetUtils.hiddenModalPopup();
                        this.widget.actionModel.sendComment(
                            this._article.channelId,
                            text,
                            Snowflake.CreateNonce(),
                            commentId
                        );
                    })
                );
            }
        }
        
        public void didPopNext() {
            StatusBarManager.statusBarStyle(false);
        }

        public void didPush() {
        }

        public void didPop() {
        }

        public void didPushNext() {
        }
    }
}