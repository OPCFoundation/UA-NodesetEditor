namespace CESMII.ProfileDesigner.DAL
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Security.Claims;
    using System.Threading.Tasks;

    using CESMII.ProfileDesigner.DAL.Models;
    using CESMII.ProfileDesigner.Data.Entities;

    public class UserToken
    {
        public int UserId { get; set; }
        public int? TargetTenantId { get; set; } // For now: set to 0 to to write globally, otherwise write to user's scope
        public static UserToken GetGlobalUser(UserToken userToken)
        {
            return new UserToken { UserId = userToken.UserId, TargetTenantId = 0, };
        }
        public override string ToString()
        {
            if (TargetTenantId != null)
            {
                return $"{UserId} ({TargetTenantId})";
            }
            return $"{UserId}";
        }
    }

    public interface IDal<TEntity, TModel>: IDisposable where TEntity : AbstractEntity where TModel: AbstractModel
    {
        TModel GetById(int id, UserToken userToken);
        /// <summary>
        /// Get item by id - asynchronously
        /// </summary>
        /// <param name="id"></param>
        /// <param name="userToken"></param>
        /// <returns></returns>
        Task<TModel> GetByIdAsync(int id, UserToken userToken);

        /// <param name="verbose">Optional. If false, this can provide the option for the implementing class to return a subset of data with less
        ///         relational tables being loaded. For my lists, I typically don't need all the child collections, lookup table info, etc. 
        ///         This can speed stuff up when getting lists of data. </param>
        List<TModel> GetAll(UserToken userToken, bool verbose = false);
        /// <summary>
        /// Provide flexibility to page the query on the repo before it is converted to list so that the executed query is performant.
        /// Return List<typeparamref name="TModel"/> and the count of rows in case paging is used
        /// </summary>
        /// <param name="skip">Optional. Paging support. Start index. Note this is 0-based so first record is 0.</param>
        /// <param name="take">Optional. Paging support. Page length. </param>
        /// <param name="returnCount">Optional. Should this also execute a separate count query to get # of rows of non-paged data.  </param>
        /// <param name="verbose">Optional. If false, this can provide the option for the implementing class to return a subset of data with less
        ///         relational tables being loaded. For my lists, I typically don't need all the child collections, lookup table info, etc. 
        ///         This can speed stuff up when getting lists of data. </param>
        /// <returns></returns>
        DALResult<TModel> GetAllPaged(UserToken userToken, int? skip = null, int? take = null, 
            bool returnCount = false, bool verbose = false);
        Task<int?> AddAsync(TModel model, UserToken userId);
        /// <summary>
        /// Update an entity asynchronously
        /// </summary>
        /// <param name="model"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        Task<int?> UpdateAsync(TModel model, UserToken userId);

        /// <summary>
        /// Asynchronously update an entity if it exists, add if not
        /// </summary>
        /// <param name="model"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        Task<(int?, bool)> UpsertAsync(TModel model, UserToken userId, bool updateExisting = true);
        Task<TModel> GetExistingAsync(TModel model, UserToken userId, bool cacheOnly = false);

        Task<int?> DeleteAsync(int id, UserToken userToken);

        Task<int> DeleteManyAsync(List<int> ids, UserToken userToken);

        /// <summary>
        /// Provide flexibility to filter on the repo before it is converted to list so that the executed query is performant.
        /// Return List<typeparamref name="TModel"/> and the count of rows in case paging is used
        /// </summary>
        /// <param name="predicate">Linq expression</param>
        /// <param name="skip">Optional. Paging support. Start index. Note this is 0-based so first record is 0.</param>
        /// <param name="take">Optional. Paging support. Page length. </param>
        /// <param name="returnCount">Optional. Should this also execute a separate count query to get # of rows of non-paged data.  </param>
        /// <param name="verbose">Optional. If false, this can provide the option for the implementing class to return a subset of data with less
        ///         relational tables being loaded. For my lists, I typically don't need all the child collections, lookup table info, etc. 
        ///         This can speed stuff up when getting lists of data. </param>
        /// <returns></returns>
        DALResult<TModel> Where(Expression<Func<TEntity, bool>> predicate, UserToken userId, int? skip = null, int? take = null, 
            bool returnCount = false, bool verbose = false);

        /// <summary>
        /// Build a list of where clauses and pass those into DAL and then to repo. 
        /// Repo will build up a query and then execute with all where clauses and order by conditions included. 
        /// Return List<typeparamref name="TModel"/> and the count of rows in case paging is used
        /// </summary>
        /// <param name="predicates">Collection of Linq expressions</param>
        /// <param name="skip">Optional. Paging support. Start index. Note this is 0-based so first record is 0.</param>
        /// <param name="take">Optional. Paging support. Page length. </param>
        /// <param name="verbose">Optional. If false, this can provide the option for the implementing class to return a subset of data with less
        ///         relational tables being loaded. For my lists, I typically don't need all the child collections, lookup table info, etc. 
        ///         This can speed stuff up when getting lists of data. </param>
        /// <returns></returns>
        DALResult<TModel> Where(List<Expression<Func<TEntity, bool>>> predicates, UserToken userId, int? skip, int? take, 
            bool returnCount = false, bool verbose = false, params OrderByExpression<TEntity>[] orderByExpressions);

        /// <summary>
        /// Get a count - if predicate is null, get all count. Otherwise, get count of mathcing items
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        int Count(Expression<Func<TEntity, bool>> predicate, UserToken userToken);
        int Count(UserToken userToken);

        void StartTransaction();
        Task CommitTransactionAsync();
        void RollbackTransaction();
        Task LoadIntoCacheAsync(Expression<Func<TEntity, bool>> predicate);
    }

    public static class DalExtensions
    {
        public static TModel GetByFunc<TEntity, TModel>(this IDal<TEntity, TModel> dal, Expression<Func<TEntity, bool>> predicate, UserToken userToken, bool verbose) where TEntity : AbstractEntity where TModel: AbstractModel
        {
            return dal.Where(predicate, userToken, null, 1, false, verbose)?.Data?.FirstOrDefault();
        }
        public static List<TModel> GetAllByFunc<TEntity, TModel>(this IDal<TEntity, TModel> dal, Expression<Func<TEntity, bool>> predicate, UserToken userToken, bool verbose) where TEntity : AbstractEntity where TModel : AbstractModel
        {
            return dal.Where(predicate, userToken, null, null, false, verbose)?.Data;
        }
    }

}