﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

using BetterCms.Core.DataAccess;
using BetterCms.Core.DataContracts.Enums;
using BetterCms.Core.Web;
using BetterCms.Module.Blog.Models;
using BetterCms.Module.Root.Models;

using BlogML;
using BlogML.Xml;

namespace BetterCms.Module.Blog.Services
{
    public class DefaultBlogMLExportService : BlogMLWriterBase, IBlogMLExportService
    {
        private List<BlogPost> posts;

        private readonly IHttpContextAccessor httpContextAccessor;
        
        private readonly IRepository repository;

        public DefaultBlogMLExportService(IHttpContextAccessor httpContextAccessor, IRepository repository)
        {
            this.httpContextAccessor = httpContextAccessor;
            this.repository = repository;
        }

        /// <summary>
        /// Exports the blog posts.
        /// </summary>
        /// <param name="postsToExport">The posts to export.</param>
        /// <returns>
        /// string in blogML format
        /// </returns>
        public string ExportBlogPosts(List<BlogPost> postsToExport)
        {
            posts = postsToExport;

            var builder = new StringBuilder();
            var xml = XmlWriter.Create(builder);

            Write(xml);

            return builder.ToString();
        }

        protected override void InternalWriteBlog()
        {
            WriteStartBlog("Better CMS", ContentTypes.Text, "Better CMS", ContentTypes.Text, httpContextAccessor.MapPath("/") ?? "/", GetMinBlogPostDate());

            WriteAuthors();
            WriteCategories();
            WritePosts();

            WriteEndElement();
            Writer.Flush();
        }

        private DateTime GetMinBlogPostDate()
        {
            var firstBlogPostDate = repository
                .AsQueryable<BlogPost>()
                .OrderBy(b => b.CreatedOn)
                .Select(b => b.CreatedOn)
                .FirstOrDefault();

            if (firstBlogPostDate != DateTime.MinValue)
            {
                return firstBlogPostDate.Date;
            }

            return DateTime.Now.Date;
        }

        private void WriteAuthors()
        {
            WriteStartAuthors();
            foreach (var author in posts.Where(p => p.Author != null).Select(p => p.Author).Distinct())
            {
                WriteAuthor(
                    author.Id.ToString(),
                    author.Name,
                    null,
                    author.CreatedOn,
                    author.ModifiedOn,
                    true);
            }
            WriteEndElement(); // </authors>
        }

        private void WriteCategories()
        {
            WriteStartCategories();
            foreach (var category in posts.Where(p => p.Category != null).Select(p => p.Category).Distinct())
            {
                WriteCategory(category.Id.ToString(), 
                    category.Name, 
                    ContentTypes.Text, 
                    category.CreatedOn, 
                    category.ModifiedOn, 
                    true, 
                    null, 
                    null);
            }
            WriteEndElement();
        }

        private void WritePosts()
        {
            WriteStartPosts();
            foreach (var post in posts)
            {
                WriteStartBlogMLPost(post);
                if (post.Category != null)
                {
                    WritePostCategory(post.Category);
                }
                if (post.Author != null)
                {
                    WritePostAuthor(post.Author);
                }

                WriteEndElement(); // </post>
                Writer.Flush();
            }
            WriteEndElement();
        }

        protected void WriteStartBlogMLPost(BlogPost post)
        {
            WriteStartElement("post");
            WriteNodeAttributes(post.Id.ToString(), post.CreatedOn, post.ModifiedOn, post.Status == PageStatus.Published);
            WriteAttributeString("post-url", post.PageUrl);
            WriteAttributeStringRequired("type", "normal");
            WriteAttributeStringRequired("hasexcerpt", (!string.IsNullOrWhiteSpace(post.Description)).ToString());
            WriteAttributeStringRequired("views", "0");
            WriteContent("title", BlogMLContent.Create(post.MetaTitle ?? post.Title, ContentTypes.Text));
            WriteContent("post-name", BlogMLContent.Create(post.Title, ContentTypes.Text));

            if (!string.IsNullOrWhiteSpace(post.Description))
            {
                WriteBlogMLContent("excerpt", BlogMLContent.Create(post.Description, ContentTypes.Text));
            }

            var content = post.PageContents.Where(pc => pc.Content is BlogPostContent).Select(pc => pc.Content).FirstOrDefault();
            if (content != null)
            {
                WriteBlogMLContent("content", BlogMLContent.Create(((BlogPostContent)content).Html, ContentTypes.Text));
            }
        }

        protected void WriteBlogMLContent(string elementName, BlogMLContent content)
        {
            WriteContent(elementName, content);
        }

        protected void WritePostCategory(Category category)
        {
            WriteStartCategories();
            WriteCategoryReference(category.Id.ToString());
            WriteEndElement();
        }

        private void WritePostAuthor(Author author)
        {
            WriteStartAuthors();
            WriteAuthorReference(author.Id.ToString());
            WriteEndElement();
        }
    }
}