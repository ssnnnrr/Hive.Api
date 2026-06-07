using Hive.Api.Data;
using Hive.Api.DTOs;
using Hive.Api.Entities;
using Hive.Api.Services;
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
        private readonly ModerationService _moderation;

        public ReviewsController(HiveDbContext context, ModerationService moderation)
        {
            _context = context;
            _moderation = moderation;
        }

        private long CurrentUserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        [HttpPost]
        public async Task<IActionResult> LeaveOrUpdateReview([FromBody] LeaveReviewRequest req)
        {
            if (CurrentUserId == req.ReviewedId)
                return BadRequest("Нельзя оценивать самого себя.");

            if (!_moderation.IsTextClean(req.Comment))
            {
                return BadRequest("Отзыв содержит недопустимую лексику.");
            }

            var review = await _context.Reviews
                .FirstOrDefaultAsync(r => r.ReviewerId == CurrentUserId && r.ReviewedId == req.ReviewedId);

            if (review != null)
            {
                review.Rating = req.Rating;
                review.Comment = req.Comment;
                review.CreatedAt = DateTime.UtcNow;
                _context.Reviews.Update(review);
            }
            else
            {
                _context.Reviews.Add(new Review
                {
                    ReviewerId = CurrentUserId,
                    ReviewedId = req.ReviewedId,
                    Rating = req.Rating,
                    Comment = req.Comment,
                    CreatedAt = DateTime.UtcNow
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
            
            if (review == null) return NotFound("Отзыв не найден.");

            _context.Reviews.Remove(review);
            await _context.SaveChangesAsync();
            return Ok();
        }



    }
}