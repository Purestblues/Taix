using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShiJian.Client.Shared.Models;

namespace ShiJian.Client.Shared.Servicers.Interfaces;

public interface ICategorys
{

    event Action? CategoriesChanged;

    Task<List<CategoryModel>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<CategoryModel?> GetCategoryAsync(int id, CancellationToken cancellationToken = default);

    Task<CategoryModel> CreateAsync(CategoryModel category);

    Task UpdateAsync(CategoryModel category);

    Task<CategoryModel> RestoreSystemCategoryAsync(int id);

    Task DeleteAsync(CategoryModel category);

    Task<int> ApplyDirectoryMatchAsync(string[]? patterns = null);

    Task<int> ApplyAutoClassifyAsync();

    void RefreshCache();
}
