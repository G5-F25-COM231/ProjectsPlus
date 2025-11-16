using Microsoft.AspNetCore.Mvc;

namespace t5f25sdprojectone_projectsplus.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
