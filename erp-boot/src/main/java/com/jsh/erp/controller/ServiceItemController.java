package com.jsh.erp.controller;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.base.BaseController;
import com.jsh.erp.datasource.entities.ServiceItem;
import com.jsh.erp.datasource.entities.User;
import com.jsh.erp.service.UserService;
import com.jsh.erp.service.cashier.ServiceItemService;
import com.jsh.erp.utils.BaseResponseInfo;
import com.jsh.erp.utils.Constants;
import io.swagger.annotations.Api;
import io.swagger.annotations.ApiOperation;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.web.bind.annotation.*;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@RestController
@RequestMapping(value = "/cashier/serviceItem")
@Api(tags = {"服务项目"})
public class ServiceItemController extends BaseController {
    private Logger logger = LoggerFactory.getLogger(ServiceItemController.class);

    @Resource
    private ServiceItemService serviceItemService;

    @Resource
    private UserService userService;

    @GetMapping(value = "/list")
    @ApiOperation(value = "获取服务项目列表")
    public BaseResponseInfo getList(@RequestParam(value = Constants.SEARCH, required = false) String search,
                                    @RequestParam("currentPage") Integer currentPage,
                                    @RequestParam("pageSize") Integer pageSize,
                                    HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        Map<String, Object> map = new HashMap<>();
        try {
            String name = null;
            Boolean enabled = null;
            if (search != null) {
                JSONObject obj = JSONObject.parseObject(search);
                name = obj.getString("name");
                if (obj.containsKey("enabled")) {
                    enabled = obj.getBoolean("enabled");
                }
            }
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            List<ServiceItem> list = serviceItemService.select(name, enabled, tenantId, (currentPage - 1) * pageSize, pageSize);
            Long total = serviceItemService.count(name, enabled, tenantId);
            map.put("rows", list);
            map.put("total", total);
            res.code = 200;
            res.data = map;
        } catch (Exception e) {
            logger.error("获取服务项目列表失败，search={}, currentPage={}, pageSize={}", search, currentPage, pageSize, e);
            res.code = 500;
            res.data = "获取数据失败：" + e.getMessage();
        }
        return res;
    }

    @PostMapping(value = "/add")
    @ApiOperation(value = "新增服务项目")
    public BaseResponseInfo add(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = serviceItemService.insert(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "新增失败";
        }
        return res;
    }

    @PutMapping(value = "/update")
    @ApiOperation(value = "修改服务项目")
    public BaseResponseInfo update(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = serviceItemService.update(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "修改失败";
        }
        return res;
    }

    @DeleteMapping(value = "/delete")
    @ApiOperation(value = "删除服务项目")
    public BaseResponseInfo delete(@RequestParam("id") Long id, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = serviceItemService.delete(id, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "删除失败";
        }
        return res;
    }

    @DeleteMapping(value = "/deleteBatch")
    @ApiOperation(value = "批量删除服务项目")
    public BaseResponseInfo deleteBatch(@RequestParam("ids") String ids, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = serviceItemService.deleteBatch(ids, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "删除失败";
        }
        return res;
    }

    private Long resolveTenantId(User userInfo) {
        if (userInfo == null) {
            return null;
        }
        if ("admin".equals(userInfo.getLoginName())) {
            return null;
        }
        if (userInfo.getTenantId() == null || userInfo.getTenantId() == 0L) {
            return null;
        }
        return userInfo.getTenantId();
    }
}
