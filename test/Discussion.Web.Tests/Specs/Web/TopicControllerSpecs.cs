﻿using Discussion.Web.Controllers;
using Discussion.Web.Models;
using Discussion.Web.Data;
using Microsoft.AspNet.Mvc;
using System.Collections.Generic;
using Xunit;
using Discussion.Web.ViewModels;
using System.Linq;
using System;

namespace Discussion.Web.Tests.Specs.Web
{

    [Collection("AppSpecs")]
    public class TopicControllerSpecs
    {
        public Application _myApp;
        public TopicControllerSpecs(Application app)
        {
            _myApp = app;
        }


        [Theory]
        [InlineData("List")]
        [InlineData("Create")]
        public void should_serve_pages_as_view_result(string actionName)
        {
            var topicController = _myApp.CreateController<TopicController>();

            var topicListResult = topicController.InvokeAction(actionName, null);

            topicListResult.ShouldNotBeNull();
            topicListResult.IsType<ViewResult>();
        }


        [Fact]
        public void should_serve_topic_list_on_page()
        {
            var topicItems = new[]
            {
                new Topic {Title = "dummy topic 1" },
                new Topic {Title = "dummy topic 2" },
                new Topic {Title = "dummy topic 3" },
            };
            var repo = _myApp.GetService<IDataRepository<Topic>>();
            foreach(var item in topicItems)
            {
                repo.Create(item);
            }


            var topicController = _myApp.CreateController<TopicController>();

            var topicListResult = topicController.List() as ViewResult;
            var topicList = topicListResult.ViewData.Model as IList<Topic>;

            topicList.ShouldNotBeNull();
            topicList.ShouldContain(t => t.Title == "dummy topic 1");
            topicList.ShouldContain(t => t.Title == "dummy topic 2");
            topicList.ShouldContain(t => t.Title == "dummy topic 3");
        }

        [Fact]
        public void should_create_topic()
        {
            var topicController = _myApp.CreateController<TopicController>();


            var model = new TopicCreationModel() { Title = "first topic you created", Content = "**This is the content of this markdown**\r\n* markdown content is greate*" };
            topicController.CreateTopic(model);


            var repo = _myApp.GetService<IDataRepository<Topic>>();
            var allTopics = repo.All.ToList();

            var createdTopic = allTopics.Find(topic => topic.Title == model.Title);

            createdTopic.ShouldNotBeNull();
            createdTopic.Title.ShouldEqual(model.Title);
            createdTopic.Content.ShouldEqual(model.Content);

            var createdAt = (DateTime.UtcNow - createdTopic.CreatedAt);
            Assert.True(createdAt.TotalMilliseconds > 0);
            Assert.True(createdAt.TotalMinutes < 2);

            createdTopic.LastRepliedAt.ShouldBeNull();
            createdTopic.ReplyCount.ShouldEqual(0);
            createdTopic.ViewCount.ShouldEqual(0);
        }

        [Fact]
        public void should_show_topic()
        {
            var topic = new Topic { Title = "dummy topic 1" };
            var repo = _myApp.GetService<IDataRepository<Topic>>();
            repo.Create(topic);


            var topicController = _myApp.CreateController<TopicController>();
            var result = topicController.Index(topic.Id) as ViewResult;


            result.ShouldNotBeNull();

            var viewModel = result.ViewData.Model;
            var topicShown = viewModel as Topic;
            topicShown.ShouldNotBeNull();
            topicShown.Id.ShouldEqual(topic.Id);
        }


    }
}