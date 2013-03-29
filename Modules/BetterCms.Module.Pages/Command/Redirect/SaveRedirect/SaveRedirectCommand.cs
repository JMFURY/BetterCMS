﻿using BetterCms.Api;
using BetterCms.Core.Exceptions.Mvc;
using BetterCms.Core.Mvc;
using BetterCms.Core.Mvc.Commands;
using BetterCms.Module.Pages.Content.Resources;
using BetterCms.Module.Pages.Services;
using BetterCms.Module.Pages.ViewModels.SiteSettings;
using BetterCms.Module.Root.Mvc;

namespace BetterCms.Module.Pages.Command.Redirect.SaveRedirect
{
    public class SaveRedirectCommand : CommandBase, ICommand<SiteSettingRedirectViewModel, SiteSettingRedirectViewModel>
    {
        /// <summary>
        /// The redirect service
        /// </summary>
        private readonly IRedirectService redirectService;

        /// <summary>
        /// Initializes a new instance of the <see cref="SaveRedirectCommand" /> class.
        /// </summary>
        /// <param name="redirectService">The redirect service.</param>
        public SaveRedirectCommand(IRedirectService redirectService)
        {
            this.redirectService = redirectService;
        }

        /// <summary>
        /// Executes a command to save a redirect.
        /// </summary>
        /// <param name="request">The redirect view model.</param>
        /// <returns>
        /// Saved redirect view model.
        /// </returns>
        public SiteSettingRedirectViewModel Execute(SiteSettingRedirectViewModel request)
        {
            Models.Redirect redirect;

            request.PageUrl = redirectService.FixUrl(request.PageUrl);
            request.RedirectUrl = redirectService.FixUrl(request.RedirectUrl);

            // Validate request
            if (!redirectService.ValidateUrl(request.PageUrl))
            {
                var message = PagesGlobalization.SaveRedirect_InvalidPageUrl_Message;
                var logMessage = string.Format("Invalid page url {0}.", request.PageUrl);
                throw new ValidationException(() => message, logMessage);
            }
            if (!redirectService.ValidateUrl(request.RedirectUrl))
            {
                var message = PagesGlobalization.SaveRedirect_InvalidRedirectUrl_Message;
                var logMessage = string.Format("Invalid redirect url {0}.", request.RedirectUrl);
                throw new ValidationException(() => message, logMessage);
            }

            // Validate for hidden segments
            string invalidSegment;
            if (!redirectService.ValidateUrlForHiddenSegments(request.PageUrl, out invalidSegment))
            {
                var message = string.Format(PagesGlobalization.SaveRedirect_PageUrlContainsHiddenSegment_Message, invalidSegment);
                var logMessage = string.Format("Page Url {0} contains hidden segment {1}.", request.PageUrl, invalidSegment);
                throw new ValidationException(() => message, logMessage);
            }
            if (!redirectService.ValidateUrlForHiddenSegments(request.RedirectUrl, out invalidSegment))
            {
                var message = string.Format(PagesGlobalization.SaveRedirect_RedirectUrlContainsHiddenSegment_Message, invalidSegment);
                var logMessage = string.Format("Redirect Url {0} contains hidden segment {1}.", request.RedirectUrl, invalidSegment);
                throw new ValidationException(() => message, logMessage);
            }

            redirectService.ValidateRedirectExists(request.PageUrl, request.Id);
            redirectService.ValidateForCircularLoop(request.PageUrl, request.RedirectUrl, request.Id);

            if (request.Id.HasDefaultValue())
            {
                redirect = new Models.Redirect();
            }
            else
            {
                redirect = Repository.First<Models.Redirect>(request.Id);
            }

            redirect.Version = request.Version;
            redirect.PageUrl = request.PageUrl;
            redirect.RedirectUrl = request.RedirectUrl;

            Repository.Save(redirect);
            UnitOfWork.Commit();

            // Notify.
            if (request.Id.HasDefaultValue())
            {
                PagesApiContext.Events.OnRedirectCreated(redirect);
            }
            else
            {
                PagesApiContext.Events.OnRedirectUpdated(redirect);
            }

            return new SiteSettingRedirectViewModel
            {
                Id = redirect.Id,
                Version = redirect.Version,
                PageUrl = redirect.PageUrl,
                RedirectUrl = redirect.RedirectUrl
            };
        }
    }
}