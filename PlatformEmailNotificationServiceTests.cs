using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Mail;
using Microsoft.VisualStudio.Services.Account;
using Microsoft.VisualStudio.Services.EmailNotification;
using Microsoft.VisualStudio.Services.Identity;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.TeamFoundation.Framework.Server;
using Microsoft.VisualStudio.Services.Licensing;
using MS.VS.Test.Services.EmailNotification.UnitTests;
using System.IO;
using Microsoft.VisualStudio.Services.Profile;

namespace MS.TF.Test.VisualStudioServices.EmailNotification
{

    public class PlatformEmailNotificationServiceTests
    {
        [TestClass]
        public class ServiceStart
        {
            [TestMethod]
            public void ServiceStart_MustCallNotificationSettingsManagerStart()
            {
                var mockRequestContext = TestableEmailNotificationRequestContext.CreateValidMockDeploymentUserRequestContext();
                var testablePlatformEmailNotificationService = TestablePlatformEmailNotificationService.Create(mockRequestContext);
                testablePlatformEmailNotificationService.ServiceStart(mockRequestContext.Object);

                mockRequestContext.SettingsManager.Verify(x => x.Start(mockRequestContext.Object));
            }

            [TestMethod]
            public void ServiceEnd_MustCallNotificationSettingsManagerStop()
            {
                var mockRequestContext = TestableEmailNotificationRequestContext.CreateValidMockDeploymentUserRequestContext();
                var testablePlatformEmailNotificationService = TestablePlatformEmailNotificationService.Create(mockRequestContext);
                testablePlatformEmailNotificationService.ServiceEnd(mockRequestContext.Object);

                mockRequestContext.SettingsManager.Verify(x => x.Stop(It.IsAny<IVssRequestContext>()));
            }

            [TestMethod]
            public void ServiceStart_MustCallNotificationSettingsManagerStartAccountContext()
            {
                var mockRequestContext = TestableEmailNotificationRequestContext.CreateValidMockAccountUserRequestContext();
                var testablePlatformEmailNotificationService = TestablePlatformEmailNotificationService.Create(mockRequestContext);
                testablePlatformEmailNotificationService.ServiceStart(mockRequestContext.Object);

                mockRequestContext.SettingsManager.Verify(x => x.Start(It.IsAny<IVssRequestContext>()));
            }
        }

        [TestClass]
        public class SendEmailNotification
        {
            [TestMethod]
            public void SendEmailNotificationPositiveTest()
            {
                var mockRequestContext = TestableEmailNotificationRequestContext.CreateValidMockDeploymentUserRequestContext();
                var settingManager = TestableEmailNotificationSettingsManager.Create(mockRequestContext);
                settingManager.Populate();
                var testablePlatformEmailNotificationService = TestablePlatformEmailNotificationService.Create(mockRequestContext, settingManager);
                 var data = new ConfirmNewEmailNotificationEmailData
                {
                    DisplayName = "Manish Ojha",
                    ConfirmationUrl = "http://link.com?sdsad=sadsad",
                    NewEmail = "emailAddress@sdadasd.com"
                };

                 testablePlatformEmailNotificationService.SendEmailNotification(mockRequestContext.Object, new MailAddress("testemail@email.com"), data);
                 mockRequestContext.MockTeamFoundationMailService.Verify(m => m.QueueMailJob(It.IsAny<IVssRequestContext>(), It.IsAny<MailMessage>()));
            }
        }
    }
}