using Hive.Api.Data;
using Hive.Api.DTOs;
using Hive.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Hive.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ReviewsController : ControllerBase
    {
        private readonly HiveDbContext _context;
        public ReviewsController(HiveDbContext context) => _context = context;

        private long CurrentUserId => long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        [HttpPost]
        public async Task<IActionResult> LeaveOrUpdateReview(ReviewRequest req)
        {
            if (CurrentUserId == req.ReviewedId) return BadRequest("Нельзя оценивать себя");

            var review = await _context.Reviews
                .FirstOrDefaultAsync(r => r.ReviewerId == CurrentUserId && r.ReviewedId == req.ReviewedId);

            if (review != null)
            {
                // РЕДАКТИРОВАНИЕ
                review.Rating = req.Rating;
                review.Comment = req.Comment;
                review.CreatedAt = DateTime.UtcNow;
            }
            else
            {
                // СОЗДАНИЕ
                _context.Reviews.Add(new Review
                {
                    ReviewerId = CurrentUserId,
                    ReviewedId = req.ReviewedId,
                    Rating = req.Rating,
                    Comment = req.Comment
                });
            }
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("{targetUserId}")]
        public async Task<IActionResult> DeleteReview(long targetUserId)
        {
            var review = await _context.Reviews
                .FirstOrDefaultAsync(r => r.ReviewerId == CurrentUserId && r.ReviewedId == targetUserId);

            if (review == null) return NotFound();
            _context.Reviews.Remove(review);
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}