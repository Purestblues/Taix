using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShiJian.Client.Librarys.Api;
using ShiJian.Client.Shared.Models;
using ShiJian.Client.Shared.Servicers.Interfaces;

namespace ShiJian.Client.Servicers.Instances;

public class ApiCategorys : ICategorys
{
    private readonly IShiJianApiClient _apiClient;
    private List<CategoryModel>? _cachedCategories;

    public ApiCategorys(IShiJianApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public event Action? CategoriesChanged;

    public async Task<List<CategoryModel>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedCategories != null)
            return _cachedCategories;

        _cachedCategories = await _apiClient.GetCategoriesAsync(cancellationToken);
        return _cachedCategories;
    }

    public void RefreshCache()
    {
        _cachedCategories = null;
        // 通知下游分类已变更
        CategoriesChanged?.Invoke();
    }

    public async Task<CategoryModel?> GetCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _apiClient.GetCategoryAsync(id, cancellationToken);
    }

    public async Task<CategoryModel> CreateAsync(CategoryModel category)
    {
        var result = await _apiClient.CreateCategoryAsync(category);
        RefreshCache();
        return result;
    }

    public async Task UpdateAsync(CategoryModel category)
    {
        await _apiClient.UpdateCategoryAsync(category);
        RefreshCache();
    }

    public async Task<CategoryModel> RestoreSystemCategoryAsync(int id)
    {
        var result = await _apiClient.RestoreSystemCategoryAsync(id);
        RefreshCache();
        return result;
    }

    public async Task DeleteAsync(CategoryModel category)
    {
        if (category.IsSystem) return;
        await _apiClient.DeleteCategoryAsync(category.ID);
        RefreshCache();
    }

    public async Task<int> ApplyDirectoryMatchAsync(string[]? patterns = null)
    {
        return await _apiClient.ApplyDirectoryMatchAsync(patterns);
    }
}
