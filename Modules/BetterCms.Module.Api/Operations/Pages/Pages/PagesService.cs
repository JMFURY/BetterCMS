﻿using System;
using System.Collections.Generic;
using System.Linq;

using BetterCms.Core.DataAccess;
using BetterCms.Core.DataContracts.Enums;
using BetterCms.Core.Security;

using BetterCms.Module.Api.Helpers;
using BetterCms.Module.Api.Infrastructure;
using BetterCms.Module.Api.Operations.Pages.Pages.Search;
using BetterCms.Module.Api.Operations.Root;
using BetterCms.Module.MediaManager.Services;
using BetterCms.Module.Pages.Models;
using BetterCms.Module.Root.Models;
using BetterCms.Module.Root.Services;

using NHibernate.Linq;

using ServiceStack.ServiceInterface;

using AccessLevel = BetterCms.Module.Api.Operations.Root.AccessLevel;

namespace BetterCms.Module.Api.Operations.Pages.Pages
{
    public class PagesService : Service, IPagesService
    {
        private readonly IRepository repository;
        
        private readonly IOptionService optionService;

        private readonly IMediaFileUrlResolver fileUrlResolver;
        
        private readonly ISearchPagesService searchPagesService;
        
        private readonly IAccessControlService accessControlService;

        public PagesService(IRepository repository, IOptionService optionService, IMediaFileUrlResolver fileUrlResolver,
            ISearchPagesService searchPagesService, IAccessControlService accessControlService)
        {
            this.repository = repository;
            this.optionService = optionService;
            this.fileUrlResolver = fileUrlResolver;
            this.searchPagesService = searchPagesService;
            this.accessControlService = accessControlService;
        }

        public GetPagesResponse Get(GetPagesRequest request)
        {
            request.Data.SetDefaultOrder("Title");

            var query = repository
                .AsQueryable<PageProperties>();

            if (!request.Data.IncludeArchived)
            {
                query = query.Where(b => !b.IsArchived);
            }

            if (!request.Data.IncludeUnpublished)
            {
                query = query.Where(b => b.Status == PageStatus.Published);
            }

            if (!request.Data.IncludeMasterPages)
            {
                query = query.Where(b => !b.IsMasterPage);
            }

            query = query.ApplyPageTagsFilter(request.Data);

            if (request.User != null && !string.IsNullOrWhiteSpace(request.User.Name))
            {
                var principal = new ApiPrincipal(request.User);
                IEnumerable<Guid> deniedPages = accessControlService.GetPrincipalDeniedObjects<PageProperties>(principal, false);
                foreach (var deniedPageId in deniedPages)
                {
                    var id = deniedPageId;
                    query = query.Where(f => f.Id != id);
                }
            }

            var includeMetadata = request.Data.IncludeMetadata;
            var listResponse = query
                .Select(page => new PageModel
                    {
                        Id = page.Id,
                        Version = page.Version,
                        CreatedBy = page.CreatedByUser,
                        CreatedOn = page.CreatedOn,
                        LastModifiedBy = page.ModifiedByUser,
                        LastModifiedOn = page.ModifiedOn,

                        PageUrl = page.PageUrl,
                        Title = page.Title,
                        Description = page.Description,
                        IsPublished = page.Status == PageStatus.Published,
                        PublishedOn = page.PublishedOn,
                        LayoutId = page.Layout != null && !page.Layout.IsDeleted ? page.Layout.Id : (Guid?)null,
                        MasterPageId = page.MasterPage != null && !page.MasterPage.IsDeleted ? page.MasterPage.Id : (Guid?)null,
                        CategoryId = page.Category != null && !page.Category.IsDeleted ? page.Category.Id : (Guid?)null,
                        CategoryName = page.Category != null && !page.Category.IsDeleted ? page.Category.Name : null,
                        MainImageId = page.Image != null && !page.Image.IsDeleted ? page.Image.Id : (Guid?)null,
                        MainImageUrl = page.Image != null && !page.Image.IsDeleted ? page.Image.PublicUrl : null,
                        MainImageThumbnauilUrl = page.Image != null && !page.Image.IsDeleted ? page.Image.PublicThumbnailUrl : null,
                        MainImageCaption = page.Image != null && !page.Image.IsDeleted ? page.Image.Caption : null,
                        IsArchived = page.IsArchived,
                        IsMasterPage = page.IsMasterPage,
                        LanguageId = page.Language != null ? page.Language.Id : (Guid?)null,
                        LanguageCode = page.Language != null ? page.Language.Code : null,
                        LanguageGroupIdentifier = page.LanguageGroupIdentifier,
                        Metadata = includeMetadata 
                            ? new MetadataModel
                                  {
                                      MetaDescription = page.MetaDescription,
                                      MetaTitle = page.MetaTitle,
                                      MetaKeywords = page.MetaKeywords,
                                      UseNoFollow = page.UseNoFollow,
                                      UseNoIndex = page.UseNoIndex,
                                      UseCanonicalUrl = page.UseCanonicalUrl
                                  } : null
                    }).ToDataListResponse(request);

            foreach (var model in listResponse.Items)
            {
                model.MainImageUrl = fileUrlResolver.EnsureFullPathUrl(model.MainImageUrl);
                model.MainImageThumbnauilUrl = fileUrlResolver.EnsureFullPathUrl(model.MainImageThumbnauilUrl);
            }

            if (listResponse.Items.Count > 0
                && (request.Data.IncludePageOptions || request.Data.IncludeTags || request.Data.IncludeAccessRules))
            {
                LoadInnerCollections(listResponse, request.Data.IncludePageOptions, request.Data.IncludeTags, request.Data.IncludeAccessRules);
            }

            return new GetPagesResponse
            {
                Data = listResponse
            };
        }

        private void LoadInnerCollections(DataListResponse<PageModel> response, bool includeOptions, bool includeTags, bool includeAccessRules)
        {
            var pageIds = response.Items.Select(i => i.Id).Distinct().ToArray();

            IEnumerable<TagModel> tagsFuture;
            if (includeTags)
            {
                tagsFuture = repository.AsQueryable<PageTag>(pt => pageIds.Contains(pt.Page.Id))
                        .Select(pt => new TagModel { PageId = pt.Page.Id, Tag = pt.Tag.Name })
                        .OrderBy(o => o.Tag)
                        .ToFuture();
            }
            else
            {
                tagsFuture = null;
            }

            IEnumerable<AccessRuleModelEx> rulesFuture;
            if (includeAccessRules)
            {
                rulesFuture = (from page in repository.AsQueryable<Module.Root.Models.Page>()
                    from accessRule in page.AccessRules
                    where pageIds.Contains(page.Id)
                    orderby accessRule.IsForRole, accessRule.Identity
                    select new AccessRuleModelEx
                           {
                               AccessRule = new AccessRuleModel
                               {
                                   AccessLevel = (AccessLevel)(int)accessRule.AccessLevel,
                                   Identity = accessRule.Identity,
                                   IsForRole = accessRule.IsForRole
                               },
                               PageId = page.Id
                           })
                    .ToFuture();
            }
            else
            {
                rulesFuture = null;
            }

            if (tagsFuture != null)
            {
                var tags = tagsFuture.ToList();
                response.Items.ToList().ForEach(page => 
                { 
                    page.Tags = tags
                        .Where(tag => tag.PageId == page.Id)
                        .Select(tag => tag.Tag)
                        .ToList();
                });
            }

            if (rulesFuture != null)
            {
                var rules = rulesFuture.ToList();
                response.Items.ToList().ForEach(page => 
                {
                    page.AccessRules = rules
                        .Where(rule => rule.PageId == page.Id)
                        .Select(rule => rule.AccessRule)
                        .ToList();
                });
            }

            if (includeOptions)
            {
                response.Items.ForEach(
                    page =>
                        {
                            page.Options = optionService
                                .GetMergedMasterPagesOptionValues(page.Id, page.MasterPageId, page.LayoutId)
                                .Select(o => new OptionModel
                                    {
                                        Key = o.OptionKey,
                                        Value = o.OptionValue,
                                        DefaultValue = o.OptionDefaultValue,
                                        Type = ((Root.OptionType)(int)o.Type)
                                    })
                                .ToList();
                        });
            }
        }

        private class LayoutWithOption
        {
            public Guid LayoutId { get; set; }

            public LayoutOption Option { get; set; }
        }
        
        private class PageWithOption
        {
            public Guid PageId { get; set; }

            public PageOption Option { get; set; }
        }

        private class TagModel
        {
            public Guid PageId { get; set; }

            public string Tag { get; set; }
        }

        private class AccessRuleModelEx
        {
            public AccessRuleModel AccessRule { get; set; }
            public Guid PageId { get; set; }
        }

        SearchPagesResponse IPagesService.Search(SearchPagesRequest request)
        {
            return searchPagesService.Get(request);
        }
    }
}